using System.Collections.ObjectModel;
using CoreBluetooth;


namespace Blex.Touch.Ble
{
    internal class PeripheralManager : CBPeripheralDelegate
    {
        private readonly Dictionary<string, CBCharacteristic> _discoveredCharacteristics;

        private TaskCompletionSource<bool>? _discoverGATT;
        private TaskCompletionSource<byte[]>? _readCharPromise;
        private readonly IList<string> _subscribees;
        private TaskCompletionSource<bool>? _writeCharPromise;


        private CBPeripheral Peripheral { get; set; }
        public event EventHandler<CBCharacteristic> CharacteristicUpdated;

        private int _serviceCount;

        public PeripheralManager(CBPeripheral peripheral)
        {
            _discoverGATT = null;
            _readCharPromise = null;
            _writeCharPromise = null;
            _serviceCount = 0;
            _discoveredCharacteristics = new Dictionary<string, CBCharacteristic>();
            _subscribees = new Collection<string>();
            
            Peripheral = peripheral;
            Peripheral.DiscoveredService += DiscoveredService;
            Peripheral.DiscoveredCharacteristics += DiscoveredCharacteristics;
            Peripheral.WroteCharacteristicValue += WroteCharacteristicValue;
            Peripheral.UpdatedCharacterteristicValue += UpdatedCharacterteristicValue;
        }

        /// <summary>
        ///     iOS gives back the same CBPeripheral on a reconnection: the previous manager stops listening to it,
        ///     otherwise its callbacks run along with the new manager's.
        /// </summary>
        public void Detach()
        {
            Peripheral.DiscoveredService -= DiscoveredService;
            Peripheral.DiscoveredCharacteristics -= DiscoveredCharacteristics;
            Peripheral.WroteCharacteristicValue -= WroteCharacteristicValue;
            Peripheral.UpdatedCharacterteristicValue -= UpdatedCharacterteristicValue;
            _discoverGATT?.TrySetCanceled();
            _readCharPromise?.TrySetCanceled();
            _writeCharPromise?.TrySetCanceled();
            _discoverGATT = null;
            _readCharPromise = null;
            _writeCharPromise = null;
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

            var charac = _discoveredCharacteristics!.GetValueOrDefault(characteristic.ToLower(), null);

            Peripheral.WriteValue(NSData.FromArray(encrypted), charac, CBCharacteristicWriteType.WithResponse);

            return _writeCharPromise.Task;
        }

        public Task<byte[]> Read(string characteristic)
        {
            _readCharPromise = new TaskCompletionSource<byte[]>();
            var charac = _discoveredCharacteristics!.GetValueOrDefault(characteristic.ToLower(), null);
            if (charac == null) _readCharPromise.TrySetException(new Exception("Charcteristic not found"));
            else Peripheral.ReadValue(charac);
            return _readCharPromise.Task;
        }

        public void Subscribe(string characteristic)
        {
            if (_subscribees.Contains(characteristic.ToLower())) return;

            _subscribees.Add(characteristic.ToLower());
            var charac = _discoveredCharacteristics!.GetValueOrDefault(characteristic.ToLower(), null);

            Peripheral.SetNotifyValue(true, charac);
        }

        internal bool HasCharacterisitic(string characteristic)
        {
            return _discoveredCharacteristics!.GetValueOrDefault(characteristic.ToLower(), null) != null;
        }
        
        #region CBPeripheralDelegate


        private void DiscoveredService(object? sender, NSErrorEventArgs e)
        {
            if (e.Error != null)
            {
                return;
            }
            _serviceCount = Peripheral.Services!.Length;

            foreach (var service in Peripheral.Services) Peripheral.DiscoverCharacteristics(service);
        }
        
        private void DiscoveredCharacteristics(object? sender, CBServiceEventArgs e)
        {
            if (e.Error != null)
            {
                return;
            }
            foreach (var characteristic in e.Service.Characteristics!)
            {
                var charUuid = characteristic.UUID;
                _discoveredCharacteristics[charUuid.ToString().ToLower()] = characteristic;
            }

            _serviceCount--;
            if (_serviceCount == 0 && _discoverGATT != null)
            {
                _discoverGATT.TrySetResult(true);
                _discoverGATT = null;
            }
        }
        
        private void WroteCharacteristicValue(object? sender, CBCharacteristicEventArgs e)
        {
            var compl = _writeCharPromise;
            if (compl == null) return;
            _writeCharPromise = null;
        
            if (e.Error == null)
                compl.TrySetResult(true);
            else
                compl.TrySetException(new NSErrorException(e.Error));
        }
        
        private void UpdatedCharacterteristicValue(object? sender, CBCharacteristicEventArgs e)
        {
            if (_readCharPromise != null)
            {
                var compl = _readCharPromise;
                _readCharPromise = null;
        
                if (e.Error == null)
                    compl.TrySetResult(e.Characteristic.Value!.ToArray());
                else
                    compl.TrySetException(new NSErrorException(e.Error));
            }
        
            if (_subscribees.Contains(e.Characteristic.UUID.Uuid.ToLower()))
                CharacteristicUpdated?.Invoke(this, e.Characteristic);
        }
        



        #endregion
    }
}