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
using ScanMode = Android.Bluetooth.LE.ScanMode;

namespace Blex.Droid.ScanHelper
{
    internal class ScanHelperMarshmallow : ScanCallback, IScanHelperCompat
    {
        private readonly BluetoothAdapter _bluetoothAdapter;
        private readonly IList<IScanResult> _results = new List<IScanResult>();
        private Predicate<IScanResult> _filter;
        private int _limit;
        private Runnable _stopScanningAfterDelay;
        private string[] _uuids;

        public ScanHelperMarshmallow(Context context)
        {
            Handler = new Handler(Looper.MainLooper);

            // Initializes Bluetooth adapter.
            var bluetoothManager =
                (Android.Bluetooth.BluetoothManager) context.GetSystemService(Context.BluetoothService);
            _bluetoothAdapter = bluetoothManager.Adapter;
            _bluetoothAdapter.Enable();
        }

        private TaskCompletionSource<IEnumerable<IScanResult>> Completion { get; set; }
        private Handler Handler { get; }

//        public ILogger Logger { get; set; }

        public Task<IEnumerable<IScanResult>> Scan(int limit = 1000, int maxDuration = 10000,
            Predicate<IScanResult> filter = null,
            string[] uuidsRequiered = null)
        {
            if (Completion != null)
                //                Logger?.v("finish early previous scan request");
                StopScanning();

            Console.WriteLine("ScanHelperMarshmallow Scanning");


            Completion = new TaskCompletionSource<IEnumerable<IScanResult>>();

            _results.Clear();

            var scanner = _bluetoothAdapter.BluetoothLeScanner;


            _limit = limit;
            _filter = filter;
            _uuids = uuidsRequiered ?? new string[] { };
            using (var s = new ScanSettings.Builder()
                .SetMatchMode(BluetoothScanMatchMode.Aggressive)
                .SetScanMode(ScanMode.LowLatency)
                .Build())
            {
//                Logger?.d(
//                    $"Scanning for {_limit} device(s) with rssi > {_rssi} containing uuid {_uuids?.First() ?? "N/A"}");
                var filters = new List<ScanFilter>();
                scanner.StartScan(filters, s, this);
            }


            // Stops scanning after a pre-defined scan period.
            _stopScanningAfterDelay = new Runnable(StopScanning);
            Handler.PostDelayed(_stopScanningAfterDelay, maxDuration);

            return Completion.Task;
        }

        public void StopScanning()
        {
            var scanner = _bluetoothAdapter.BluetoothLeScanner;
            scanner?.StopScan(this);

            Task.Delay(250).ContinueWith(t =>
            {
                Completion?.TrySetResult(_results);

                try
                {
                    Handler.RemoveCallbacks(_stopScanningAfterDelay);
                }
                finally
                {
                    Completion = null;
                    _stopScanningAfterDelay = null;
                }
            });
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

            Console.WriteLine($"scan result = {scanresult.DeviceName} ({scanresult.RSSI}) ({string.Join(',', scanresult.AdvertisedService)})");


            if (_filter(scanresult) && _uuids.Aggregate(true,
                    (current, uuid) => current && scanresult.AdvertisedService.Contains(uuid.ToLower())))
            {
//                Logger?.d(
//                    $"Device {result.Device.Address} found with rssi : {scanresult.RSSI}, services : {string.Join(" - ", scanresult.AdvertisedService)}");

                _results.Add(scanresult);

                if (_results.Count >= _limit) StopScanning();
            }
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            base.OnScanFailed(errorCode);

            Console.WriteLine("==> Scan failed");

            if (errorCode == ScanFailure.ApplicationRegistrationFailed)
            {
                _bluetoothAdapter.Disable();
                _bluetoothAdapter.Enable();
            }

            Completion?.TrySetException(new Exception(errorCode.ToString()));
            Completion = null;
        }
    }
}