using System;

namespace Blex
{
    public enum Error
    {
        Success = 0,
        DiscoveryService,
        AuthErrorWhileBonded,
        Unknown,
        TimeOut,
        LinkLost,
        Unsupported,
        PreconditionsFailed,
        Failed
    }

    public class BluetoothException : Exception
    {
        public readonly Error Error;

        public BluetoothException(Error error)
        {
            Error = error;
        }

        public override string Message => $"{Error}";
    }
}