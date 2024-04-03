using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Java.Lang;
using Object = Java.Lang.Object;

namespace Blex.Droid.ScanHelper
{
    internal class ScanHelperJB : Object, IScanHelperCompat, BluetoothAdapter.ILeScanCallback
    {
        private readonly IList<IScanResult> Results = new List<IScanResult>();
        private Runnable StopScanningAfterDalay;

        public ScanHelperJB(Context context)
        {
            Handler = new Handler(Looper.MainLooper);
            Adapter = BluetoothAdapter.DefaultAdapter;
        }

        public BluetoothAdapter Adapter { get; set; }

        private TaskCompletionSource<IEnumerable<IScanResult>> Completion { get; set; }
        private int Limit { get; set; }

        private string[] SeekedUUIds { get; set; }
        private Handler Handler { get; }

        private Predicate<IScanResult> Filter { get; set; }

        public void OnLeScan(BluetoothDevice device, int rssi, byte[] scanRecord)
        {
            var data = ScanRecord.Parse(scanRecord);

            var scanresult = new ScanResult
            {
                NativeDevice = device,
                RSSI = rssi,
                DeviceName = device.Name ?? "N/A",
                AdvertisedService = data.ServiceUuids?.Select(x => x.ToString().ToLower()).ToList() ??
                                    new List<string>(),
                TimeStamp = new DateTime()
            };

//            Logger?.v(Class.Name + " : " + "Scan result : " + scanresult);

            if (!Filter(scanresult) || !SeekedUUIds.Aggregate(true,
                    (current, uuid) => current && scanresult.AdvertisedService.Contains(uuid.ToLower()))) return;
            Results.Add(scanresult);

            if (Results.Count < Limit) return;

            Handler.RemoveCallbacksAndMessages(null);
            StopScanning();
        }

//        public ILogger Logger { get; set; }

        public Task<IEnumerable<IScanResult>> Scan(int limit = 1000, int maxDuration = 10000,
            Predicate<IScanResult> filter = null,
            string[] uuidsRequiered = null)
        {
            if (Completion != null)
                //                Logger?.v("Scan finish early");
                StopScanning();

            //            Logger?.v(Class.Name + "Scan");

            Console.WriteLine("ScanHelperJB Scanning");

            Completion = new TaskCompletionSource<IEnumerable<IScanResult>>();
            Results.Clear();

            Limit = limit;
            Filter = filter ?? (result => true);
            SeekedUUIds = uuidsRequiered;
            Adapter.StartLeScan(this);

            // Stops scanning after a pre-defined scan period.
            StopScanningAfterDalay = new Runnable(StopScanning);
            Handler.PostDelayed(StopScanningAfterDalay, maxDuration);

            return Completion.Task;
        }

        private void StopScanning()
        {
            Adapter.StopLeScan(this);


            Completion?.TrySetResult(Results);

            try
            {
                Handler.RemoveCallbacks(StopScanningAfterDalay);
            }
            finally
            {
                Completion = null;
                StopScanningAfterDalay = null;
            }
        }
    }
}