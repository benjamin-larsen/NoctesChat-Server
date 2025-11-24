using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace NoctesChat;

public class UInt64DBSerializer : SerializerBase<UInt64>
{
    public override UInt64 Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return (UInt64)context.Reader.ReadInt64();
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, UInt64 value)
    {
        context.Writer.WriteInt64((Int64)value);
    }
}