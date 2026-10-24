using System;
using System.Linq;
using System.Text;

namespace Game.Services.Classic;

// The Classic client queries these adjacent social services during character selection.
static class ClassicSocialSupport
{
    public static BnetCall Handle(uint service, uint method, bool inWorld, ulong guild, byte[] payload)
    {
        if (service == 0x94B94786)
        {
            if (method == 2) return new();
            if (method != 1) return new() { Status = 0xBC7 };
            // An OK subscription without an attached player disconnects this client.
            if (!inWorld) return new() { Status = 1 };
            if (guild != 0) return new() { Status = 0xBC7 };
            var view = new ProtoWriter(); view.Varint(1,0); view.Varint(2,0);
            var state = new ProtoWriter(); state.Message(4,view);
            var response = new ProtoWriter(); response.Message(1,state);
            return new() { Body = response.ToArray() };
        }
        if (service != 0xE273DE0E || method != 6) return new() { Status = 0xBC7 };
        var requested = ProtoField.Walk(payload).FirstOrDefault(field => field.Field == 2 && field.Wire == 2);
        string name = requested.Bytes == null ? "" : ProtoField.Walk(requested.Bytes)
            .Where(field => field.Field == 2 && field.Wire == 2).Select(field => Encoding.UTF8.GetString(field.Bytes)).FirstOrDefault() ?? "";
        if (!name.Equals("guild",StringComparison.Ordinal)) return new() { Status = 0xD };
        var type = new ProtoWriter(); type.Fixed32(1,0x576F57); type.String(2,"guild");
        var body = new ProtoWriter(); body.Message(1,type);
        return new() { Body = body.ToArray() };
    }
}
