using System;
using System.Collections.Generic;

namespace Blex.Droid
{
    internal class ScanResult : IScanResult
    {
        /// <summary>
        /// The native object
        /// </summary>
        public object NativeDevice { get; set; }
        
        /// <summary>
        /// The device read RSSI
        /// </summary>
        public int RSSI { get; set;}
        
        /// <summary>
        /// The device Name
        /// </summary>
        public string DeviceName { get; set;}
        
        /// <summary>
        /// The timestamp of the scan result
        /// </summary>
        public DateTime TimeStamp { get; set;}
        
        public IList<string> AdvertisedService { get; set; }
    }
}