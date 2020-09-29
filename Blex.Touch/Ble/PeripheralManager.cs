using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreBluetooth;
using Foundation;

namespace Blex.Touch.Ble
{
    internal class PeripheralManager : CBPeripheralDelegate
    {
        private readonly Dictionary<string, CBCharacteristic> _discoveredCharacteristics =
            new Dictionary<string, CBCharacteristic>();

        private TaskCompletionSource<bool> _discoverGATT;
        private TaskCompletionSource<byte[]> _readCharPromise;
        private readonly IList<string> _subscribees = new List<string>();
        private TaskCompletionSource<bool> _writeCharPromise;


        private CBPeripheral Peripheral { get; set; }
        public event EventHandler<CBCharacteristic> CharacteristicUpdated;


        public void Reset(CBPeripheral peripheral)
        {
            _discoverGATT = null;
            _readCharPromise = null;
            _writeCharPromise = null;

            _discoveredCharacteristics.Clear();
            _subscribees.Clear();

            Peripheral = peripheral;
            peripheral.Delegate = this;
        }

        public Task DiscoverGATT()
        {
            _discoverGATT = new TaskCompletionSource<bool>();
            _discoveredCharacteristics.Clear();
            Peripheral.DiscoverServices();
            return _discoverGATT.Task;
        }

        public Task Write(byte[] encrypted, string characteristic)
        {
            _writeCharPromise = new TaskCompletionSource<bool>();

            var charac = _discoveredCharacteristics.GetValueOrDefault(characteristic.ToLower(), null);

            Peripheral.WriteValue(NSData.FromArray(encrypted), charac, CBCharacteristicWriteType.WithResponse);

            return _writeCharPromise.Task;
        }

        public Task<byte[]> Read(string characteristic)
        {
            _readCharPromise = new TaskCompletionSource<byte[]>();
            var charac = _discoveredCharacteristics.GetValueOrDefault(characteristic.ToLower(), null);
            if (charac == null) _readCharPromise.TrySetException(new Exception("Charcteristic not found"));
            Peripheral.ReadValue(charac);
            return _readCharPromise.Task;
        }

        public void Subscribe(string characteristic)
        {
            if (_subscribees.Contains(characteristic.ToLower())) return;

            _subscribees.Add(characteristic.ToLower());
            var charac = _discoveredCharacteristics.GetValueOrDefault(characteristic.ToLower(), null);

            Peripheral.SetNotifyValue(true, charac);
        }

        #region CBPeripheralDelegate

        private int serviceCount;

        public override void DiscoveredService(CBPeripheral peripheral, NSError error)
        {
            serviceCount = Peripheral.Services.Length;

            foreach (var service in Peripheral.Services) Peripheral.DiscoverCharacteristics(service);
        }

        public override void DiscoveredCharacteristic(CBPeripheral peripheral, CBService service, NSError error)
        {
            foreach (var characteristic in service.Characteristics)
            {
                var charUuid = characteristic.UUID;
                _discoveredCharacteristics.Add(charUuid.ToString().ToLower(), characteristic);
            }

            serviceCount--;
            if (serviceCount == 0 && _discoverGATT != null)
            {
                _discoverGATT.SetResult(true);
                _discoverGATT = null;
            }
        }

        internal bool HasCharacterisitic(string characteristic)
        {
            return _discoveredCharacteristics.GetValueOrDefault(characteristic.ToLower(), null) != null;
        }

        public override void WroteCharacteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic,
            NSError error)
        {
            var compl = _writeCharPromise;
            if (compl == null) return;
            _writeCharPromise = null;

            if (error == null)
                compl.SetResult(true);
            else
                compl.SetException(new NSErrorException(error));
        }

        public override void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic,
            NSError error)
        {
            if (_readCharPromise != null)
            {
                var compl = _readCharPromise;
                _readCharPromise = null;

                if (error == null)
                    compl.SetResult(characteristic.Value.ToArray());
                else
                    compl?.SetException(new NSErrorException(error));
            }

            if (_subscribees.Contains(characteristic.UUID.Uuid.ToLower()))
                CharacteristicUpdated?.Invoke(this, characteristic);
        }

        #endregion
    }
}