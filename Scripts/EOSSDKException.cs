using System;
using Epic.OnlineServices;

namespace EpicTransport
{
    public class EOSSDKException : Exception
    {
        public EOSSDKException(Result result, string message) : base($"(Result.{result}) {message}") { }
    }
}
