using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Blex.Droid.ScanHelper
{
    internal interface IScanHelperCompat
    {
//        ILogger Logger { get; set; }

        Task<IEnumerable<IScanResult>> Scan(int limit = 1000, int maxDuration = 10000,
            Predicate<IScanResult> filter = null,
            string[] uuidsRequiered = null);
    }
}