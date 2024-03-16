﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Player
{
    [Message(Opcodes.PlayerLevel)]
    public class PlayerLevel : GameMessage
    {
        public PlayerLevel() : base(Opcodes.PlayerLevel) { }

        public int PlayerIndex;
        public int Level;

        

        public override void Parse(GameBitBuffer buffer)
        {
            PlayerIndex = buffer.ReadInt(3);
            Level = buffer.ReadInt(15);
        }

        public override void Encode(GameBitBuffer buffer)
        {
            buffer.WriteInt(3, PlayerIndex);
            buffer.WriteInt(15, Level);
        }

        public override void AsText(StringBuilder b, int pad)
        {
            b.Append(' ', pad);
            b.AppendLine("PlayerLevel:");
            b.Append(' ', pad++);
            b.AppendLine("{");
            b.Append(' ', pad); b.AppendLine("PlayerIndex: 0x" + PlayerIndex.ToString("X8") + " (" + PlayerIndex + ")");
            b.Append(' ', pad); b.AppendLine("Level: 0x" + Level.ToString("X8") + " (" + Level + ")");
            b.Append(' ', --pad);
            b.AppendLine("}");
        }
    }
}
