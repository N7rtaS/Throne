using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace ThornThrone.Progression
{
    /// <summary>Why a save file is not whole: file name, failed check (header, length, checksum, json, schema, field, io), field and message.</summary>
    public sealed class DecodeError
    {
        public readonly string File;
        public readonly string Check;
        public readonly string Field;
        public readonly string Message;

        public DecodeError(string file, string check, string field, string message)
        {
            File = file;
            Check = check;
            Field = field;
            Message = message;
        }

        public override string ToString() => Field == null ? $"{File}: {Check}: {Message}" : $"{File}: {Check} {Field}: {Message}";
    }

    /// <summary>
    /// Save container (Mechanics §19, Р): ASCII line «THORNSAVE 1 &lt;JSON length in bytes&gt; &lt;lowercase hex SHA-256 of JSON&gt;\n»,
    /// then the profile JSON in UTF-8 without BOM.
    /// </summary>
    public static class SaveFormat
    {
        public const string Magic = "THORNSAVE";
        public const int ContainerVersion = 1;
        private const int MaxHeaderBytes = 128;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(Profile profile) => Wrap(ProfileJson.ToJson(profile));

        /// <summary>Wraps arbitrary JSON text with a valid header (also used by tests to build damaged profiles).</summary>
        public static byte[] Wrap(string json)
        {
            var body = Utf8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes(
                $"{Magic} {ContainerVersion.ToString(CultureInfo.InvariantCulture)} {body.Length.ToString(CultureInfo.InvariantCulture)} {Sha256Hex(body)}\n");
            var bytes = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            Buffer.BlockCopy(body, 0, bytes, header.Length, body.Length);
            return bytes;
        }

        /// <summary>
        /// Checks header, length, checksum, JSON, schema (with migrations) and fields. Returns false with
        /// <paramref name="error"/> if the file is not whole; <paramref name="migrated"/> tells that migration steps ran.
        /// </summary>
        public static bool TryDecode(string file, byte[] bytes, ProfileKind kind, ProfileMigrations migrations,
            out Profile profile, out bool migrated, out DecodeError error)
        {
            profile = null;
            migrated = false;
            error = null;
            try
            {
                int newline = Array.IndexOf(bytes, (byte)'\n', 0, Math.Min(bytes.Length, MaxHeaderBytes));
                if (newline < 0) throw new ProfileFormatException("header", null, "No header line");
                var parts = Encoding.ASCII.GetString(bytes, 0, newline).Split(' ');
                if (parts.Length != 4 || parts[0] != Magic)
                    throw new ProfileFormatException("header", null, "Not a ThornThrone save");
                if (parts[1] != ContainerVersion.ToString(CultureInfo.InvariantCulture))
                    throw new ProfileFormatException("header", null, $"Unknown container version '{parts[1]}'");
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int length))
                    throw new ProfileFormatException("header", null, "Bad length");
                int bodyLength = bytes.Length - newline - 1;
                if (length != bodyLength)
                    throw new ProfileFormatException("length", null, $"Header length {length}, actual {bodyLength}");
                var body = new byte[bodyLength];
                Buffer.BlockCopy(bytes, newline + 1, body, 0, bodyLength);
                if (!string.Equals(parts[3], Sha256Hex(body), StringComparison.Ordinal))
                    throw new ProfileFormatException("checksum", null, "SHA-256 mismatch");
                string json;
                try
                {
                    json = Utf8.GetString(body);
                }
                catch (DecoderFallbackException e)
                {
                    throw new ProfileFormatException("json", null, e.Message);
                }
                var obj = ProfileJson.ParseObject(json);
                migrated = migrations.Migrate(obj);
                profile = ProfileJson.FromObject(obj, kind, migrations.Current);
                return true;
            }
            catch (ProfileFormatException e)
            {
                profile = null;
                migrated = false;
                error = new DecodeError(file, e.Check, e.Field, e.Message);
                return false;
            }
        }

        private static string Sha256Hex(byte[] body)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(body);
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }
}
