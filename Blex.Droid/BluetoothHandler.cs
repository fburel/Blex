using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Blex.Droid.Ble;
using Blex.Droid.ScanHelper;
using Trace = System.Diagnostics.Trace;


namespace Blex.Droid
{
    public class BluetoothHandler : IBluetoothHandler
    {
        
        public event EventHandler<string> LogReceived;

        
        private const double ConnectionTimeOutSeconds = 30;

        
        private readonly Dictionary<string, byte[]> _archivedData = new Dictionary<string, byte[]>();
        private readonly IScanHelperCompat _helper;

        private readonly PeripheralManager _peripheralMnager;

        private readonly IList<string> _subscribedCharacteristic = new List<string>();

        private readonly Dictionary<string, IList<Characteristicupdated>> _subscriber =
            new Dictionary<string, IList<Characteristicupdated>>();
        
        public BluetoothHandler(Context context)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                _helper = new ScanHelperMarshmallow(context);
            else if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                _helper = new ScanHelperLollipop(context);
            else
                _helper = new ScanHelperJB(context);

            _peripheralMnager = new PeripheralManager(context);
            _peripheralMnager.CharacteristicUpdated += OncharacteristicUpdated;
            _peripheralMnager.ConnectionStateChanged += OnConnectionStateChanged;
        }
        
        public IEncryptionHandler EncryptionHandler { get; set; }


        public async Task<IEnumerable<IScanResult>> Scan(int limit, TimeSpan maxDuration, Predicate<IScanResult>? filter,
            string[]? uuidsRequiered)
        { 
            Log($"scanning");
            
            var results = await _helper.Scan(limit, (int) maxDuration.TotalMilliseconds, filter ?? (x => true), uuidsRequiered);
            
            return results;
        }

        public async Task<IEnumerable<IScanResult>> Scan(int limit, int maxDuration, int RSSILimit, string[] uuidsRequiered)
        { 
            Log($"scanning");

            var results = await _helper.Scan(limit, maxDuration, s => s.RSSI >= RSSILimit, uuidsRequiered);
            
            return results;
        }

        #region Connect

        public event EventHandler<bool> ConnectionStatusChanged;

        public bool IsConnected => _peripheralMnager.IsConnected;

        public Task Connect(IScanResult nativeDevice)
        {
            Log($"connecting");

            var device = nativeDevice.NativeDevice as BluetoothDevice;

            return _peripheralMnager.Connect(device, ConnectionTimeOutSeconds);
        }

        public Task Disconnect()
        {
            Log($"disconnecting");
            return _peripheralMnager.Disconnect().ContinueWith(t => true);
        }

        private void OnConnectionStateChanged(object sender, State e)
        {
            Log($"{e.ToString()} event received");
            if (e == State.Connecting || e == State.Disconnecting) return; // do not pass "in progress" notification
            Log("OnConnectionStateChanged event passed");
            ConnectionStatusChanged?.Invoke(this, e == State.Connected);
        }

        public Task DiscoverGATT()
        {
            _subscribedCharacteristic.Clear();
            _archivedData.Clear();
            return Task.FromResult(true);
        }


        public bool HasDiscoveredCharacteristics(string characteristic)
        {
            return IsConnected &&
                   _peripheralMnager.DiscoveredCharacteristics.GetValueOrDefault(characteristic.ToLower(), null) !=
                   null;
        }

        #endregion

        #region Read / Write

        public Task WriteValue(byte[] value, string characteristic)
        {
            Log($"writing : {BitConverter.ToString(value)}");

            var encrypted = EncryptionHandler != null
                ? EncryptionHandler.EncryptMessage(value, characteristic)
                : value;
            return _peripheralMnager.Write(encrypted, characteristic);
        }

        public async Task<byte[]> ReadData(string characteristic)
        {
            var data = await _peripheralMnager.Read(characteristic);
            var decrypted = EncryptionHandler != null
                ? EncryptionHandler.DecryptMessage(data, characteristic)
                : data;
            _archivedData[characteristic] = decrypted;
            return decrypted;
        }

        public void WriteWithoutResponse(byte[] value, string characteristic)
        {
            var encrypted = EncryptionHandler != null
                ? EncryptionHandler.EncryptMessage(value, characteristic)
                : value;
            _peripheralMnager.Write(encrypted, characteristic, true);
        }

        #endregion

        #region Subscription

        public byte[] ArchivedValue(string characteristic)
        {
            return _archivedData.GetValueOrDefault(characteristic, new byte[0]);
        }

        private readonly object _subscribeLock = new object();

        public async Task Subscribe(string characteristic, Characteristicupdated @object)
        {
            if (!_subscribedCharacteristic.Contains(characteristic.ToLower()))
            {
                lock (_subscribeLock)
                {
                    _subscribedCharacteristic.Add(characteristic.ToLower());
                }

                await _peripheralMnager.Subscribe(characteristic);
            }

            lock (_subscribeLock)
            {
                var subscribers =
                    new List<Characteristicupdated>(_subscriber.GetValueOrDefault(characteristic.ToLower(),
                        new List<Characteristicupdated>()))
                    {
                        @object
                    };
                _subscriber[characteristic.ToLower()] = subscribers;
            }
        }

        public void Unsubscribe(Characteristicupdated @object)
        {
            lock (_subscribeLock)
            {
                var ss = new Dictionary<string, IList<Characteristicupdated>>(_subscriber);

                foreach (var keyValuePair in ss)
                {
                    var characteristic = keyValuePair.Key;

                    var subs = new List<Characteristicupdated>(keyValuePair.Value);
                    if (subs.Contains(@object))
                    {
                        subs.Remove(@object);
                        _subscriber[characteristic.ToLower()] = subs;
                    }
                }
            }
        }

        private void OncharacteristicUpdated(object sender, BluetoothGattCharacteristic characteristic)
        {
            lock (_subscribeLock)
            {
                var uuid = characteristic.Uuid.ToString().ToLower();
                var data = characteristic.GetValue();
                
                Log("Data received:  " + BitConverter.ToString(data).Replace("-", ""));
                
                var decrypted = EncryptionHandler != null
                    ? EncryptionHandler.DecryptMessage(data, uuid)
                    : data;
                Log("Data received (decrypted) :  " + BitConverter.ToString(decrypted).Replace("-", ""));
                
                _archivedData[uuid] = decrypted;
                var subscribers =
                    new List<Characteristicupdated>(_subscriber.GetValueOrDefault(uuid,
                        new List<Characteristicupdated>()));
                foreach (var characteristicupdated in subscribers) characteristicupdated.Invoke(uuid);
            }
        }

        void Log(string txt)
        {
#if DEBUG
            Trace.WriteLine(txt);
#endif
            LogReceived?.Invoke(this, txt);
            
        }

        #endregion
    }
}