using System;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal interface IPackageListRequest
    {
        bool IsCompleted { get; }

        bool IsSuccess { get; }

        string ErrorMessage { get; }

        IEnumerable<PackageManagerPackageInfo> Packages { get; }
    }

    internal interface IPackageListClient
    {
        IPackageListRequest List(bool offlineMode, bool includeIndirectDependencies);
    }

    internal sealed class UnityPackageListClient : IPackageListClient
    {
        public IPackageListRequest List(bool offlineMode, bool includeIndirectDependencies)
        {
            return new UnityPackageListRequest(Client.List(offlineMode, includeIndirectDependencies));
        }

        private sealed class UnityPackageListRequest : IPackageListRequest
        {
            private readonly ListRequest _request;

            public UnityPackageListRequest(ListRequest request)
            {
                _request = request;
            }

            public bool IsCompleted => _request != null && _request.IsCompleted;

            public bool IsSuccess => _request != null && _request.Status == StatusCode.Success;

            public string ErrorMessage => _request != null && _request.Error != null
                ? _request.Error.message
                : "Package Manager returned an unknown error.";

            public IEnumerable<PackageManagerPackageInfo> Packages
            {
                get
                {
                    if (_request == null || _request.Result == null)
                    {
                        return Array.Empty<PackageManagerPackageInfo>();
                    }

                    return _request.Result;
                }
            }
        }
    }
}
