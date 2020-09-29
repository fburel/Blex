using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreBluetooth;
using CoreFoundation;
using Foundation;

namespace Blex.Touch.Ble
{
    internal class ConnectionHelper : CBCentralManagerDelegate
    {
        private static readonly ulong NanoSecondsPerSeconds = 1000000000ul;


        private readonly IList<IScanResult> _devicesFound = new List<IScanResult>();
        private TaskCompletionSource<bool> _connectCompletion;

        private TaskCompletionSource<bool> _disconnectCompletion;
        private Predicate<IScanResult> _filter;
        private int _limite;
        private TaskCompletionSource<IEnumerable<IScanResult>> _scanCompletionSource;

        public ConnectionHelper()
        {
            Manager = new CBCentralManager(this, DispatchQueue.MainQueue);
        }


        public CBCentralManager Manager { get; }

        public CBPeripheral Peripheral { get; set; }


        public event EventHandler<bool> ConnectionStateChanged;

        public async Task<CBCentralManagerState> PowerOnCentralManagerIfNeeded(CBCentralManager manager)
        {
            if (manager.State != CBCentralManagerState.PoweredOn)
            {
                Manager.ScanForPeripherals(peripheralUuids: null);

                await Task.Delay(1000);

                Manager.StopScan();
            }

            return manager.State;
        }

        public Task<IEnumerable<IScanResult>> Scan(int limit = 1000, int maxDuration = 10000,
            Predicate<IScanResult> filter = null, string[] uuidsRequiered = null)
        {
            if (_scanCompletionSource != null) return _scanCompletionSource.Task;

            _devicesFound.Clear();

            _scanCompletionSource = new TaskCompletionSource<IEnumerable<IScanResult>>();
            _limite = limit;
            _filter = filter ?? (t => true);


            PowerOnCentralManagerIfNeeded(Manager).ContinueWith(t =>
            {
                Manager.ScanForPeripherals(uuidsRequiered.Select(CBUUID.FromString).ToArray());

                Task.Delay(maxDuration).ContinueWith(x => { StopScanning(); });
            });

            return _scanCompletionSource.Task;
        }

        private void StopScanning()
        {
            if (_scanCompletionSource == null) return;

            Manager.StopScan();

            _scanCompletionSource.SetResult(_devicesFound.AsEnumerable());

            _scanCompletionSource = null;
        }

        public Task<bool> Connect(CBPeripheral peripheral)
        {
            //var connectedDevices = _manager.RetrievePeripheralsWithIdentifiers(new NSUuid(peripheral.UUID.ToString()));

            //foreach (var connectedDevice in connectedDevices)
            //{
            //    _manager.CancelPeripheralConnection(connectedDevice);
            //}

            _connectCompletion = new TaskCompletionSource<bool>();
            Manager.ConnectPeripheral(peripheral);
            return _connectCompletion.Task;
        }

        public Task<bool> Disconnect(CBPeripheral peripheral)
        {
            if (peripheral != null)
                return Task.FromResult(true);
            _disconnectCompletion = new TaskCompletionSource<bool>();

            return _disconnectCompletion.Task;
        }


        #region CBCentralMAnagerDelegate

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral,
            NSDictionary advertisementData, NSNumber RSSI)
        {
            var result = new ScanResult
            {
                NativeDevice = peripheral,
                RSSI = RSSI.Int32Value,
                DeviceName = peripheral.Name ??
                             advertisementData.ObjectForKey(CBAdvertisement.DataLocalNameKey)?.Description ?? "N/A",
                TimeStamp = new DateTime()
            };

            if (!_filter(result)) return;

            _devicesFound.Add(result);
            if (_devicesFound.Count >= _limite) StopScanning();
        }

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
        {
            Peripheral = peripheral;
            if (_connectCompletion != null)
            {
                _connectCompletion.SetResult(true);
                _connectCompletion = null;
            }

            ConnectionStateChanged?.Invoke(this, true);
        }

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
        {
            Peripheral = null;
            if (_disconnectCompletion != null)
            {
                _disconnectCompletion.SetResult(true);
                _disconnectCompletion = null;
            }

            ConnectionStateChanged?.Invoke(this, false);
        }

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
        {
            Peripheral = null;
            if (_connectCompletion != null)
            {
                _connectCompletion.SetException(new NSErrorException(error));
                _connectCompletion = null;
            }
        }

        public override void UpdatedState(CBCentralManager central)
        {
        }

        #endregion
    }
}