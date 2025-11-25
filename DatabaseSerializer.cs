using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace NoctesChat;

public class UInt64DBSerializer : SerializerBase<ulong>
{
    public override ulong Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return (ulong)context.Reader.ReadInt64();
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ulong value)
    {
        context.Writer.WriteInt64((long)value);
    }
}

public class BinU64DBSerializer : SerializerBase<ulong>
{
    public override ulong Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return Utils.UInt64FromBin(context.Reader.ReadBytes());
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ulong value)
    {
        context.Writer.WriteBytes(Utils.UInt64ToBin(value));
    }
}