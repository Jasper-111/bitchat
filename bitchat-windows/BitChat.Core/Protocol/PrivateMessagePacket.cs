using System.Text;

namespace BitChat.Core.Protocol;

public class PrivateMessagePacket
{
    public string MessageID { get; }
    public string Content { get; }

    public PrivateMessagePacket(string messageId, string content)
    {
        MessageID = messageId;
        Content = content;
    }

    public byte[] Encode()
    {
        var idBytes = Encoding.UTF8.GetBytes(MessageID);
        var contentBytes = Encoding.UTF8.GetBytes(Content);
        var result = new byte[2 + idBytes.Length + 2 + contentBytes.Length];
        int pos = 0;

        result[pos++] = 0x00; // messageID type
        result[pos++] = (byte)idBytes.Length;
        Buffer.BlockCopy(idBytes, 0, result, pos, idBytes.Length);
        pos += idBytes.Length;

        result[pos++] = 0x01; // content type
        result[pos++] = (byte)contentBytes.Length;
        Buffer.BlockCopy(contentBytes, 0, result, pos, contentBytes.Length);

        return result;
    }

    public static PrivateMessagePacket? Decode(byte[] data)
    {
        int pos = 0;
        string? messageId = null;
        string? content = null;

        while (pos < data.Length)
        {
            if (pos + 2 > data.Length) break;
            var type = data[pos++];
            var len = data[pos++];
            if (pos + len > data.Length) break;
            if (type == 0x00)
                messageId = Encoding.UTF8.GetString(data, pos, len);
            else if (type == 0x01)
                content = Encoding.UTF8.GetString(data, pos, len);
            pos += len;
        }

        if (messageId == null || content == null) return null;
        return new PrivateMessagePacket(messageId, content);
    }
}
