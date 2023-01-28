﻿using bgs.protocol;
using DiIiS_NA.Core.Helpers.Hash;
using DiIiS_NA.Core.Logging;
using DiIiS_NA.GameServer.ClientSystem;
using DiIiS_NA.LoginServer.AccountsSystem;
using DiIiS_NA.LoginServer.Base;
using DiIiS_NA.LoginServer.ChannelSystem;
using DiIiS_NA.LoginServer.Objects;
using DiIiS_NA.LoginServer.ServicesSystem;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Google.ProtocolBuffers;
using Google.ProtocolBuffers.DescriptorProtos;
using Google.ProtocolBuffers.Descriptors;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Threading.Tasks;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Text;

namespace DiIiS_NA.LoginServer.Battle
{
    public class BattleClient : SimpleChannelInboundHandler<BNetPacket>, IRpcChannel
	{
		private static readonly Logger Logger = LogManager.CreateLogger(nameof(BattleClient));

		public Dictionary<uint, uint> Services { get; private set; }
		public ISocketChannel SocketConnection { get; private set; }
		public IChannelHandlerContext Connect { get; private set; }
		public bool AuthenticationStatus = false;
		public ClientLocale ClientLanguage = ClientLocale.enUS;
		public IRpcController ListenerController;
		private uint _tokenCounter = 0;
		public static NO_RESPONSE NO_RESPONSE = NO_RESPONSE.CreateBuilder().Build();
		private readonly Dictionary<int, RPCCallBack> _pendingResponses = new(); // TODO: Check usage and remove if not needed
		public bgs.protocol.v2.Attribute AttributeOfServer { get; set; }

		public Account Account { get; set; }
		public const byte ServiceReply = 0xFE;
		public SslStream Ssl = null;
		private static int REQUEST_SERVICE_ID = 0;
		private static int RESPONSE_SERVICE_ID = 254;
		//public object clientLock = new object();
		public readonly object _serviceLock = new();
		public object messageLock = new();
		private ulong _listenerId; // last targeted rpc object.
		public bool MOTDSent { get; private set; }
		private ConcurrentDictionary<ulong, ulong> MappedObjects { get; set; }
		public bool GuildChannelsRevealed = false;
		public string GameTeamTag = "";
		public ulong CID = 0;

		#region current channel

		public Dictionary<ulong, Channel> Channels = new();

		public List<Channel> ChatChannels = new();
		public Channel PartyChannel; //Used for all non game related messages
		public Channel GameChannel; //Used for all game related messages

		public GameClient InGameClient { get; set; }

		private Channel _currentChannel;
		public Channel CurrentChannel
		{
			get
			{
				if (_currentChannel == null)
					_currentChannel = Channels.Values.FirstOrDefault(c => !c.IsChatChannel);
				return _currentChannel;
			}
			set
			{
				if (value == null)
				{
					if (_currentChannel != null)
						Channels.Remove(_currentChannel.DynamicId);
					//Logger.Trace("Client removed from CurrentChannel: {0}, setting new CurrentChannel to {1}", this._currentChannel, this.Channels.FirstOrDefault().Value);
					_currentChannel = Channels.FirstOrDefault().Value;
				}
				else if (!Channels.ContainsKey(value.DynamicId))
				{
					Channels.Add(value.DynamicId, value);
					_currentChannel = value;
				}
				else
					_currentChannel = value;
			}
		}

		
		public void SendServerWhisper(string text)
		{
			if (text.Trim() == string.Empty) return;

			var notification = bgs.protocol.notification.v1.Notification.CreateBuilder()
				.SetTargetId(Account.GameAccount.BnetEntityId)
				.SetType("WHISPER")
				.SetSenderId(Account.GameAccount.BnetEntityId)
				.SetSenderAccountId(Account.BnetEntityId)
				.AddAttribute(bgs.protocol.Attribute.CreateBuilder().SetName("whisper")
				.SetValue(Variant.CreateBuilder().SetStringValue(text).Build()).Build()).Build();

			MakeRPC((lid) => bgs.protocol.notification.v1.NotificationListener.CreateStub(this).
				OnNotificationReceived(new HandlerController()
				{
					ListenerId = lid
				}, notification, callback => { }));
		}

		public void SendServerMessage(string text)
		{
			InGameClient.SendMessage(new BroadcastTextMessage()
			{
				Field0 = text
			});
		}

		public void LeaveAllChannels()
		{
			List<Channel> _channels = Channels.Values.ToList();
			foreach (var channel in _channels)
			{
				try
				{
					channel.RemoveMember(this, Channel.RemoveReason.Left);
				}
				catch { }
			}
			Channels.Clear();
		}

		#endregion

		public BattleClient(ISocketChannel socketChannel, DotNetty.Handlers.Tls.TlsHandler TLS)
		{
			SocketConnection = socketChannel;
			Services = new Dictionary<uint, uint>();
			MappedObjects = new ConcurrentDictionary<ulong, ulong>();
			MOTDSent = false;
			if (SocketConnection.Active)
				Logger.Trace("Client - {0} - successfully encrypted the connection", socketChannel.RemoteAddress);
		}

		protected override void ChannelRead0(IChannelHandlerContext ctx, BNetPacket msg)
		{
			Connect = ctx;
			Header header = msg.GetHeader();
			byte[] payload = (byte[])msg.GetPayload();

			if (msg.GetHeader().ServiceId == RESPONSE_SERVICE_ID)
			{
				if (_pendingResponses.Count == 0) return;
				RPCCallBack done = _pendingResponses[(int)header.Token];
				if (done != null)
				{
					var service = Service.GetByID(header.ServiceId);
					if (service != null)
					{
						IMessage message = DescriptorProto.ParseFrom(payload);
						done.Action(message);
						_pendingResponses.Remove((int)header.Token);
					}
					else
						Logger.Debug(
							$"Incoming Response: Unable to identify service (id: {header.ServiceId}, hash: 0x{header.ServiceHash:04X})");
				}
			}
			else
			{
				var service = Service.GetByID(Service.GetByHash(header.ServiceHash));
				if (header.ServiceHash != 2119327385)
					if (service != null)
					{
						#region All service hashes

						/*
							AccountService - 1658456209
							AccountNotify - 1423956503
							AuthenticationClient - 1898188341
							AuthenticationServer - 233634817
							ChallengeNotify - 3151632159
							ChannelService - 3073563442
							ChannelSubscriber - 3213656212
							ChannelVoiceService - 2559626750
							ClubMembershipListener - 724851067
							ClubMembershipService - 2495170438 - Нужен
							ConnectionService - 1698982289
							DiagService - 3111080599
							FriendsService - 2749215165
							FriendsNotify - 1864735251
							PresenceListener - 2299181151
							PresenceService - 4194801407
							ReportService - 2091868617
							Resources - 3971904954
							SessionListener - 2145610546
							SessionService - 510168069
							SocialNetworkService - 1910276758
							SocialNetworkListener - 3506428651
							UserManagerService - 1041835658
							UserManagerNotify - 3162975266
							WhisperListener - 1072006302
							WhisperService - 3240634617
							ChannelMembershipService - 2119327385
							ChannelMembershipListener - 25167806
							ChannelService_v2 - 2039298513
							ChannelListener_v2 - 451225222
							ReportService_v2 - 977410299
							VoiceService_v2 - 4117798472 
	
							AccountService - 0x62DA0891
							AccountNotify - 0x54DFDA17
							AuthenticationClient - 0x71240E35
							AuthenticationServer - 0x0DECFC01
							ChallengeNotify - 0xBBDA171F
							ChannelService - 0xB732DB32
							ChannelSubscriber - 0xBF8C8094
							ChannelVoiceService - 0x9890CDFE
							ClubMembershipListener - 0x2B34597B
							ClubMembershipService - 0x94B94786
							ConnectionService - 0x65446991
							DiagService - 0xB96F5297
							FriendsService - 0xA3DDB1BD
							FriendsNotify - 0x6F259A13
							PresenceListener - 0x890AB85F
							PresenceService - 0xFA0796FF
							ReportService - 0x7CAF61C9
							Resources - ECBE75BA
							SessionListener - 0x7FE36B32
							SessionService - 0x1E688C05
							SocialNetworkService - 0x71DC8296
							SocialNetworkListener - 0xD0FFDAEB
							UserManagerService - 0x3E19268A
							UserManagerNotify - 0xBC872C22
							WhisperListener - 0x3FE5849E
							WhisperService - 0xC12828F9
							ChannelMembershipService - 0x7E525E99
							ChannelMembershipListener - 0x018007BE
							ChannelService_v2 - 0x798D39D1
							ChannelListener_v2 - 0x1AE52686
							ReportService_v2 - 0x3A4218FB
							VoiceService_v2 - 0xF5709E48
						*/

						#endregion

						MethodDescriptor method =
							service.DescriptorForType.Methods.Single(m => GetMethodId(m) == header.MethodId);
						IMessage proto = service.GetRequestPrototype(method);
						IBuilder builder = proto.WeakCreateBuilderForType();
						IMessage message = builder.WeakMergeFrom(ByteString.CopyFrom(payload)).WeakBuild();
						try
						{
							lock (service)
							{
								HandlerController controller = new()
								{
									Client = this,
									LastCallHeader = header,
									Status = 0,
									ListenerId = 0
								};
#if DEBUG
								if (method.Name == "KeepAlive")
								{
									Logger.Debug(
										$"Call: $[olive]{service.GetType().Name}$[/]$, Service hash: $[olive]{header.ServiceHash}$[/]$, Method: $[olive]{method.Name}$[/]$, ID: $[olive]{header.MethodId}$[/]$");
								}
								else
								{
									Logger.Trace(
										$"Call: $[olive]${service.GetType().Name}$[/]$, Service hash: $[olive]${header.ServiceHash}$[/]$, Method: $[olive]${method.Name}$[/]$, ID: $[olive]${header.MethodId}$[/]$");
								}
#endif

								service.CallMethod(method, controller, message,
									(IMessage m) => { sendResponse(ctx, (int)header.Token, m, controller.Status); });
							}
						}
						catch (NotImplementedException)
						{
							Logger.Warn("Unimplemented service method: {0}.{1}", service.GetType().Name, method.Name);
						}
					}
					else
					{
						Logger.Warn(
							$"Client is calling unconnected service (id: {header.ServiceId}, hash: {header.ServiceHash}  Method id: {header.MethodId})");
					}
			}
		}

		/// <summary>
		/// Platform enum for clients.
		/// </summary>
		public enum ClientPlatform
		{
			Unknown,
			Invalid,
			Win,
			Mac
		}

		/// <summary>
		/// Locale enum for clients.
		/// </summary>
		public enum ClientLocale
		{
			/// <summary>
			/// Unknown client locale state.
			/// </summary>
			Unknown,
			/// <summary>
			/// Invalid client locale.
			/// </summary>
			Invalid,
			/// <summary>
			/// Deutsch.
			/// </summary>
			deDE,
			/// <summary>
			/// English (EU)
			/// </summary>
			enGB,
			/// <summary>
			/// English (Singapore)
			/// </summary>
			enSG,
			/// <summary>
			/// English (US)
			/// </summary>
			enUS,
			/// <summary>
			/// Espanol
			/// </summary>
			esES,
			/// <summary>
			/// Espanol (Mexico)
			/// </summary>
			esMX,
			/// <summary>
			/// French
			/// </summary>
			frFR,
			/// <summary>
			/// Italian
			/// </summary>
			itIT,
			/// <summary>
			/// Korean
			/// </summary>
			koKR,
			/// <summary>
			/// Polish
			/// </summary>
			plPL,
			/// <summary>
			/// Portuguese
			/// </summary>
			ptPT,
			/// <summary>
			/// Portuguese (Brazil)
			/// </summary>
			ptBR,
			/// <summary>
			/// Russian
			/// </summary>
			ruRU,
			/// <summary>
			/// Turkish
			/// </summary>
			trTR,
			/// <summary>
			/// Chinese
			/// </summary>
			zhCN,
			/// <summary>
			/// Chinese (Taiwan)
			/// </summary>
			zhTW
		}
		public virtual void MakeTargetedRPC(RPCObject targetObject, Action<ulong> rpc)
		{
			Task.Run(() =>
			{
				//lock (this.clientLock)
				//{
				try
				{
					if (SocketConnection == null || !SocketConnection.Active) return;
					var listenerId = GetRemoteObjectId(targetObject.DynamicId);
					Logger.Debug("[RPC: {0}] Method: {1} Target: {2} [localId: {3}, remoteId: {4}].", GetType().Name, rpc.Method.Name,
								 targetObject.ToString(), targetObject.DynamicId, listenerId);

					rpc(listenerId);
				}
				catch { }
				//}
			});
		}
		public virtual void MakeRPC(Action<ulong> rpc)
		{
			Task.Run(() =>
			{
				//lock (this.clientLock)
				//{
				try
				{
					if (SocketConnection == null || !SocketConnection.Active) return;
					Logger.Debug("[RPC: {0}] Method: {1} Target: N/A", GetType().Name, rpc.Method.Name);
					rpc(0);
				}
				catch { }
				//}
			});
		}
		public void CallMethod(MethodDescriptor method, IRpcController controller, IMessage request, IMessage responsePrototype, Action<IMessage> done)
		{
			var serviceName = method.Service.FullName;
			string str = "";
			
			if (serviceName.ToLower().Contains("gamerequestlistener"))
				str = "bnet.protocol.matchmaking.GameRequestListener";
			else
				str = method.Service.Options.UnknownFields[90000].LengthDelimitedList[0].ToStringUtf8().Remove(0, 2);
			var serviceHash = StringHashHelper.HashIdentity(str);

			if (!Services.ContainsKey(serviceHash))
			{
				Logger.Warn("Service not found for client {0} [0x{1}].", serviceName, serviceHash.ToString("X8"));
				// in english: "Service not found for client {0} [0x{1}]."
				return;
			}

			uint status = 0;

			if (controller is HandlerController)
			{
				status = ((HandlerController) controller).Status;
				_listenerId = ((HandlerController) controller).ListenerId;
			}
		
			var serviceId = Services[serviceHash];
			var token = _tokenCounter++;
			sendRequest(Connect, serviceHash, GetMethodId(method), token, request, (uint)_listenerId, status);
		}
		public static void sendRequest(IChannelHandlerContext ctx, uint serviceHash, uint methodId, uint token, IMessage request, uint listenerId, uint status)
		{
			Header.Builder builder = Header.CreateBuilder();
			builder.SetServiceId((uint)REQUEST_SERVICE_ID);
			builder.SetServiceHash(serviceHash);
			builder.SetMethodId(methodId);
			if (listenerId != 0)
				builder.SetObjectId(listenerId);
			builder.SetToken(token);
			builder.SetSize((uint)request.SerializedSize);
			builder.SetStatus(status);

			ctx.Channel.WriteAndFlushAsync(new BNetPacket(builder.Build(), request));
		}
		/// <param name="localObjectId">The local objectId.</param>
		/// <param name="remoteObjectId">The remote objectId over client.</param>
		public void MapLocalObjectID(ulong localObjectId, ulong remoteObjectId)
		{
			try
			{
				MappedObjects[localObjectId] = remoteObjectId;
			}
			catch (Exception e)
			{
				Logger.WarnException(e, "MapLocalObjectID()");
			}
		}

		/// <param name="localObjectId"></param>
		public void UnmapLocalObjectId(ulong localObjectId)
		{
			try
			{
				MappedObjects.TryRemove(localObjectId, out _);
			}
			catch (Exception e)
			{
				Logger.WarnException(e, "UnmapLocalObjectID()");
			}
		}
		public ulong GetRemoteObjectId(ulong localObjectId)
		{
			return localObjectId != 0 ? MappedObjects[localObjectId] : 0;
		}
		public static uint GetMethodId(MethodDescriptor method)
		{
			try
			{
				return (uint)method.Options.UnknownFields[90000].LengthDelimitedList[0].ToByteArray()[1];
			}
			catch
			{
				return (uint)(method.Index) + 1;
			}
		}
		public static void sendResponse(IChannelHandlerContext ctx, int token, IMessage response, uint status)
		{
			Header.Builder builder = Header.CreateBuilder();
			builder.SetServiceId((uint)RESPONSE_SERVICE_ID);
			builder.SetToken((uint)token);
			builder.SetStatus(status);
			if (response != null)
				builder.SetSize((uint)response.SerializedSize);

			ctx.Channel.WriteAndFlushAsync(new BNetPacket(builder.Build(), response));
		}
		public void SendMOTD()
		{
			if (MOTDSent)
				return;

			var motd = "Welcome to BlizzLess.Net Alpha-Build Server!";

			SendServerWhisper(motd);
			MOTDSent = true;
		}

        public override void ChannelInactive(IChannelHandlerContext context)
        {
			DisconnectClient();
			base.ChannelInactive(context);
        }

		private void DisconnectClient()
		{
			if (Account != null && Account.GameAccount != null) Account.GameAccount.LoggedInClient = null;
			PlayerManager.PlayerDisconnected(this);
		}
	}
}