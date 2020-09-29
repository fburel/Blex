using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Lang;
using Exception = System.Exception;

namespace Blex.Droid.ScanHelper
{
    internal class ScanHelperLollipop : ScanCallback, IScanHelperCompat
    {
        private static readonly object _lock = new object();
        private readonly BluetoothAdapter _bluetoothAdapter;
        private readonly IList<IScanResult> _results = new List<IScanResult>();
        private Predicate<IScanResult> _filter;
        private int _limit;
        private string[] _uuids;
        private Runnable StopScanningAfterDalay;

        public ScanHelperLollipop(Context context)
        {
            Handler = new Handler(Looper.MainLooper);
            _bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
        }

        private TaskCompletionSource<IEnumerable<IScanResult>> Completion { get; set; }
        private Handler Handler { get; }


        public Task<IEnumerable<IScanResult>> Scan(int limit = 1000, int maxDuration = 10000,
            Predicate<IScanResult> filter = null,
            string[] uuidsRequiered = null)
        {
            if (Completion != null)
                //                Logger?.v("finish early previous scan request");
                StopScanning();

            Console.WriteLine("ScanHelperLollipop Scanning");

            Completion = new TaskCompletionSource<IEnumerable<IScanResult>>();

            _results.Clear();

            var scanner = _bluetoothAdapter.BluetoothLeScanner;


            _limit = limit;
            _filter = filter ?? (r => true);
            _uuids = uuidsRequiered ?? new string[] { };

//            Logger?.v(
//                $"Scanning for {_limit} device(s) with rssi > {_rssi} containing uuid {_uuids?.First() ?? "N/A"}");
            scanner.StartScan(new List<ScanFilter>(), new ScanSettings.Builder().Build(), this);


            // Stops scanning after a pre-defined scan period.
            StopScanningAfterDalay = new Runnable(() =>
            {
//                Logger?.v("Scan time out");
                StopScanning();
            });
            Handler.PostDelayed(StopScanningAfterDalay, maxDuration);

            return Completion.Task;
        }

        private void StopScanning()
        {
            _bluetoothAdapter.BluetoothLeScanner.StopScan(this);

            Completion?.TrySetResult(_results);

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

        public override void OnScanFailed(ScanFailure errorCode)
        {
            base.OnScanFailed(errorCode);

//            Logger?.v("Scan failed");

            if (errorCode == ScanFailure.ApplicationRegistrationFailed)
            {
                _bluetoothAdapter.Disable();
                _bluetoothAdapter.Enable();
            }

            Completion?.TrySetException(new Exception(errorCode.ToString()));
            Completion = null;
        }

        public override void OnScanResult(ScanCallbackType callbackType, Android.Bluetooth.LE.ScanResult result)
        {
            base.OnScanResult(callbackType, result);

            var scanresult = new ScanResult
            {
                NativeDevice = result.Device,
                RSSI = result.Rssi,
                DeviceName = result.ScanRecord?.DeviceName ?? "N/A",
                AdvertisedService = result.ScanRecord?.ServiceUuids?.Select(x => x.ToString().ToLower()).ToList() ??
                                    new List<string>(),
                TimeStamp = new DateTime()
            };

            lock (_lock)
            {
                if (StopScanningAfterDalay == null) return;

                if (_filter(scanresult) && _uuids.Aggregate(true,
                        (current, uuid) => current && scanresult.AdvertisedService.Contains(uuid.ToLower())))
                {
                    _results.Add(scanresult);

                    if (_results.Count >= _limit)
                        //                        Logger?.d(
//                            $"Device found with rssi : {scanresult.RSSI}, services : {string.Join(" - ", scanresult.AdvertisedService)}");
                        StopScanning();
                }
            }
        }
    }
}