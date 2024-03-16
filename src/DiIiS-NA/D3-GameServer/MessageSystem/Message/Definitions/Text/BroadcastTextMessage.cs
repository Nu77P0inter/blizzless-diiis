﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Text
{
    [Message(Opcodes.BroadcastTextMessage)]
    public class BroadcastTextMessage : GameMessage
    {
        public string Field0;
        public BroadcastTextMessage() : base(Opcodes.BroadcastTextMessage) { }

        public override void Parse(GameBitBuffer buffer)
        {
            Field0 = buffer.ReadCharArray(1024);
        }

        public override void Encode(GameBitBuffer buffer)
        {
            buffer.WriteCharArray(1024, Field0);
        }

        public override void AsText(StringBuilder b, int pad)
        {
            b.Append(' ', pad);
            b.AppendLine("BroadcastTextMessage:");
            b.Append(' ', pad++);
            b.AppendLine("{");
            b.Append(' ', pad); b.AppendLine("Field0: \"" + Field0 + "\"");
            b.Append(' ', --pad);
            b.AppendLine("}");
        }


    }
}
