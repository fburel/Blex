using System;

namespace Blex
{
    public interface IScanResult
    {
        /// <summary>
        /// The native object
        /// </summary>
        object NativeDevice { get; }
        
        /// <summary>
        /// The device read RSSI
        /// </summary>
        int RSSI { get; }
        
        /// <summary>
        /// The device Name
        /// </summary>
        string DeviceName { get; }
        
        /// <summary>
        /// The timestamp of the scan result
        /// </summary>
        DateTime TimeStamp { get; }
    }
}