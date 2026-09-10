using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class SourceManifestEdit
    {
        private readonly string _text;
        private readonly bool _bom;
        private readonly SourceManifestJson.Member _dependencies;

        internal SourceManifestEdit(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 4 * 1024 * 1024)
                throw new InvalidOperationException("The package manifest is missing or too large.");
            _bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
            _text = new UTF8Encoding(false, true).GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
            SourceManifestJson.Member root = new SourceManifestJson(_text).Parse();
            _dependencies = root.Members.SingleOrDefault(member => member.Name == "dependencies");
            if (_dependencies?.Members == null || _dependencies.Members.Any(member => member.Value == null))
                throw new InvalidOperationException("The manifest dependencies must be a JSON object of references.");
        }

        internal string GetReference(string packageId) => Find(packageId)?.Value;

        internal byte[] Prepare(PackageDevelopmentSession session)
        {
            SourceManifestJson.Member current = Find(session.PackageId);
            session.WasDirectDependency = current != null;
            session.OriginalReference = current?.Value;
            session.OriginalValueJson = current == null ? null : _text.Substring(current.ValueStart,
                current.End - current.ValueStart);
            ValidateReference(session.OriginalReference);
            if (current != null)
                return Bytes(Replace(current.ValueStart, current.End, SourceManifestJson.Quote(session.LocalReference)));

            int count = _dependencies.Members.Count;
            int position = count == 0 ? _dependencies.ValueStart + 1 : _dependencies.Members[count - 1].End;
            string newline = _text.Contains("\r\n") ? "\r\n" : "\n";
            session.InsertionOffset = position;
            session.InsertionText = (count == 0 ? "" : ",") + newline + "    " +
                SourceManifestJson.Quote(session.PackageId) + ": " + SourceManifestJson.Quote(session.LocalReference);
            return Bytes(_text.Insert(position, session.InsertionText));
        }

        internal byte[] Restore(PackageDevelopmentSession session, byte[] currentBytes)
        {
            SourceManifestJson.Member current = Find(session.PackageId);
            if (current == null || !string.Equals(current.Value, session.LocalReference, StringComparison.Ordinal))
                throw new InvalidOperationException("This package reference changed outside the development session. Review the manifest before restoring.");
            if (session.WasDirectDependency)
                return Bytes(Replace(current.ValueStart, current.End, session.OriginalValueJson));
            if (Hash(currentBytes) == session.ConnectedManifestHash)
            {
                if (session.InsertionOffset < 0 || string.IsNullOrEmpty(session.InsertionText) ||
                    session.InsertionOffset + session.InsertionText.Length > _text.Length ||
                    _text.Substring(session.InsertionOffset, session.InsertionText.Length) != session.InsertionText)
                    throw new InvalidOperationException("The local recovery edit is invalid. No manifest changes were made.");
                return Bytes(_text.Remove(session.InsertionOffset, session.InsertionText.Length));
            }

            int index = _dependencies.Members.IndexOf(current);
            int start = current.Start;
            int end = current.End;
            if (current.Comma >= 0) end = current.Comma + 1;
            else if (index > 0) start = _dependencies.Members[index - 1].Comma;
            return Bytes(Replace(start, end, string.Empty));
        }

        internal bool IsOriginal(PackageDevelopmentSession session)
        {
            string reference = GetReference(session.PackageId);
            return session.WasDirectDependency ? reference == session.OriginalReference : reference == null;
        }

        private SourceManifestJson.Member Find(string packageId) =>
            _dependencies.Members.SingleOrDefault(member => member.Name == packageId);

        private string Replace(int start, int end, string replacement) =>
            _text.Substring(0, start) + replacement + _text.Substring(end);

        private byte[] Bytes(string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            return _bom ? new byte[] { 239, 187, 191 }.Concat(body).ToArray() : body;
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        internal static void ValidateReference(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return;
            string candidate = reference;
            int scheme = candidate.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                int prefix = candidate.LastIndexOf('@', scheme);
                if (prefix >= 0) candidate = candidate.Substring(prefix + 1);
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) ||
                    (!string.IsNullOrEmpty(uri.UserInfo) && !(uri.Scheme == "ssh" && uri.UserInfo == "git")) ||
                    (!string.IsNullOrEmpty(uri.Query) && (!uri.Query.StartsWith("?path=", StringComparison.Ordinal) ||
                        uri.Query.IndexOf('&') >= 0 || uri.Query.IndexOf(';') >= 0)))
                    throw new InvalidOperationException("Use a credential-free package reference and existing Git authentication before starting development.");
            }
            if (reference.Any(char.IsControl) || reference.Length > 4096)
                throw new InvalidOperationException("The installed package reference is invalid.");
        }
    }
}
