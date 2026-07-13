using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Deucarian.PackageInstaller.Editor
{
    internal interface IPackageInstallRequest
    {
        bool IsCompleted { get; }

        bool IsSuccess { get; }

        string ErrorMessage { get; }

        string PackageName { get; }

        string PackageVersion { get; }
    }

    internal interface IPackageInstallClient
    {
        IPackageInstallRequest Add(string packageUrl);

        IPackageInstallRequest Remove(string packageId);
    }

    internal sealed class UnityPackageInstallClient : IPackageInstallClient
    {
        public IPackageInstallRequest Add(string packageUrl)
        {
            return new UnityAddRequest(Client.Add(packageUrl));
        }

        public IPackageInstallRequest Remove(string packageId)
        {
            return new UnityRemoveRequest(Client.Remove(packageId));
        }

        private sealed class UnityAddRequest : IPackageInstallRequest
        {
            private readonly AddRequest _request;

            public UnityAddRequest(AddRequest request)
            {
                _request = request;
            }

            public bool IsCompleted => _request != null && _request.IsCompleted;

            public bool IsSuccess => _request != null && _request.Status == StatusCode.Success;

            public string ErrorMessage => _request != null && _request.Error != null
                ? _request.Error.message
                : "Package Manager returned an unknown error.";

            public string PackageName => _request != null && _request.Result != null
                ? _request.Result.name
                : string.Empty;

            public string PackageVersion => _request != null && _request.Result != null
                ? _request.Result.version
                : string.Empty;
        }

        private sealed class UnityRemoveRequest : IPackageInstallRequest
        {
            private readonly RemoveRequest _request;

            public UnityRemoveRequest(RemoveRequest request)
            {
                _request = request;
            }

            public bool IsCompleted => _request != null && _request.IsCompleted;

            public bool IsSuccess => _request != null && _request.Status == StatusCode.Success;

            public string ErrorMessage => _request != null && _request.Error != null
                ? _request.Error.message
                : "Package Manager returned an unknown error.";

            public string PackageName => string.Empty;

            public string PackageVersion => string.Empty;
        }
    }
}
