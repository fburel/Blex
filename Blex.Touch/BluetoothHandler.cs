using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blex.Touch.Ble;
using CoreBluetooth;

namespace Blex.Touch
{
    public class BluetoothHandler : IBluetoothHandler
    {
        private readonly Dictionary<string, byte[]> _archivedData = new Dictionary<string, byte[]>();
        private readonly IList<string> _subscribedCharacteristic = new List<string>();
        private readonly ConnectionHelper ConnectionHelper;
        private readonly PeripheralManager PeripheralManager;

        private readonly Dictionary<string, IList<Characteristicupdated>> _subscriber =
            new Dictionary<string, IList<Characteristicupdated>>();

        public event EventHandler<string> LogReceived;

        public BluetoothHandler()
        {
            ConnectionHelper = new ConnectionHelper();
            ConnectionHelper.ConnectionStateChanged += OnConnectionStateChanged;
            PeripheralManager = new PeripheralManager();
            PeripheralManager.CharacteristicUpdated += OnNotificationReceived;
        }

        #region IBluetoothHandler

        private readonly object _lock = new object();

        public IEncryptionHandler EncryptionHandler { get; set; }
        
        public Task<IEnumerable<IScanResult>> Scan(int limit, TimeSpan maxDuration, Predicate<IScanResult> filter,
            string[] uuidsRequiered)
        {
            LogReceived?.Invoke(this, "scanning");
            return ConnectionHelper.Scan(limit, (int) maxDuration.TotalMilliseconds, filter, uuidsRequiered);
        }

        public Task<IEnumerable<IScanResult>> Scan(int limit, int maxDuration, int RSSILimit, string[] uuidsRequiered)
        {
            LogReceived?.Invoke(this, "scanning");
            return Scan(limit, TimeSpan.FromMilliseconds(maxDuration), t => t.RSSI >= RSSILimit, uuidsRequiered);
        }

        public bool IsConnected => ConnectionHelper.Peripheral != null;

        public async Task Connect(IScanResult scanResult)
        {
            LogReceived?.Invoke(this, "connect");

            _subscribedCharacteristic.Clear();
            _subscriber.Clear();
            var isConnected = await ConnectionHelper.Connect((CBPeripheral) scanResult.NativeDevice);
            if (isConnected) PeripheralManager.Reset(ConnectionHelper.Peripheral);
        }

        public Task Disconnect()
        {
            LogReceived?.Invoke(this, "disconnecting");

            return ConnectionHelper.Disconnect(ConnectionHelper.Peripheral);
        }

        public event EventHandler<bool> ConnectionStatusChanged;

        private void OnConnectionStateChanged(object sender, bool e)
        {
            LogReceived?.Invoke(this, "connectionStatus Changed");

            ConnectionStatusChanged?.Invoke(this, e);
        }

        public Task DiscoverGATT()
        {
            return PeripheralManager.DiscoverGATT();
        }

        public Task WriteValue(byte[] value, string characteristic)
        {
            Log($"writing : {BitConverter.ToString(value)}");
            
            var encrypted = EncryptionHandler != null
                ? EncryptionHandler.EncryptMessage(value, characteristic)
                : value;
            return PeripheralManager.Write(encrypted, characteristic);
        }

        public void WriteWithoutResponse(byte[] value, string characteristic)
        {
            var encrypted = EncryptionHandler != null
                ? EncryptionHandler.EncryptMessage(value, characteristic)
                : value;
            PeripheralManager.Write(encrypted, characteristic);
        }

        public async Task<byte[]> ReadData(string characteristic)
        {

            var data = await PeripheralManager.Read(characteristic);
            Log("ReadData received:  " + BitConverter.ToString(data).Replace("-", ""));

            var decrypted = EncryptionHandler != null
                ? EncryptionHandler.DecryptMessage(data, characteristic)
                : data;
            Log("ReadData received (decrypted):  " + BitConverter.ToString(decrypted).Replace("-", ""));

            _archivedData[characteristic] = decrypted;
            return decrypted;
        }

        public bool HasDiscoveredCharacteristics(string characteristic)
        {
            return IsConnected && PeripheralManager.HasCharacterisitic(characteristic);
        }

        public byte[] ArchivedValue(string characteristic)
        {
            return _archivedData.GetValueOrDefault(characteristic, new byte[0]);
        }

        public Task Subscribe(string characteristic, Characteristicupdated @object)
        {
            lock (_lock)
            {
                if (!_subscribedCharacteristic.Contains(characteristic))
                {
                    PeripheralManager.Subscribe(characteristic);
                    _subscribedCharacteristic.Add(characteristic.ToLower());
                }

                var subscribers =
                    _subscriber.GetValueOrDefault(characteristic.ToLower(), new List<Characteristicupdated>());
                subscribers.Add(@object);
                _subscriber[characteristic.ToLower()] = subscribers;

                return Task.FromResult(true);
            }
        }

        public void Unsubscribe(Characteristicupdated @object)
        {
            lock (_lock)
            {
                foreach (var keyValuePair in _subscriber)
                {
                    var subs = keyValuePair.Value;
                    if (subs.Contains(@object)) subs.Remove(@object);
                }
            }
        }

        private void OnNotificationReceived(object sender, CBCharacteristic e)
        {
            lock (_lock)
            {
                var uuid = e.UUID.Uuid.ToLower();
                var data = e.Value.ToArray();
                Log("Data received:  " + BitConverter.ToString(data).Replace("-", ""));

                var decrypted = EncryptionHandler != null
                    ? EncryptionHandler.DecryptMessage(data, uuid)
                    : data;
                Log("Data received (decrypted) :  " + BitConverter.ToString(decrypted).Replace("-", ""));

                _archivedData[uuid] = decrypted;
                var subscribers = _subscriber.GetValueOrDefault(uuid, new List<Characteristicupdated>()).ToList();
                foreach (var characteristicupdated in subscribers) characteristicupdated.Invoke(uuid);
            }
        }

        #endregion
        
        private void Log(string s)
        {
            LogReceived?.Invoke(this, s);
        }
    }
}