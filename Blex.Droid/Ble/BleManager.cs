using System;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Java.Util;
using Random = System.Random;
using Trace = System.Diagnostics.Trace;

namespace Blex.Droid.Ble
{
    internal static class KnownUUID
    {
        public static readonly UUID CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID =
            UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

        public static readonly UUID BATTERY_SERVICE = UUID.FromString("0000180F-0000-1000-8000-00805f9b34fb");

        public static readonly UUID BATTERY_LEVEL_CHARACTERISTIC =
            UUID.FromString("00002A19-0000-1000-8000-00805f9b34fb");

        public static readonly UUID GENERIC_ATTRIBUTE_SERVICE = UUID.FromString("00001801-0000-1000-8000-00805f9b34fb");

        public static readonly UUID SERVICE_CHANGED_CHARACTERISTIC =
            UUID.FromString("00002A05-0000-1000-8000-00805f9b34fb");
    }

    internal abstract class BleManager : BluetoothGattCallback
    {
        #region Battery

        private int _batteryValue;

        #endregion

        /**
         * The manager constructor.
         * <p>
         * After constructing the manager, the callbacks object must be set with
         * {@link #setGattCallbacks(BleManagerCallbacks)}.
         * <p>
         * To connect a device, call {@link #connect(BluetoothDevice)}.
         *
         * @param context context
         */
        public BleManager(Context context)
        {
            _context = context;
            _handler = new Handler();
            _bluetoothStateBroadcastReceiver.IntentReceived += OnBluetoothStateBroadcast;
            _bondingBroadcastReceiver.IntentReceived += OnBondingBroadcastReceived;
        }


        /**
         * This method should nullify all services and characteristics of the device.
         * It's called when the device is no longer connected, either due to user action
         * or a link loss.
         */
        protected abstract void OnServiceDiscovered(BluetoothGatt gatt);
        protected abstract void OnDeviceDisconnected(BluetoothDevice device);
        protected abstract void OnDeviceConnecting(BluetoothDevice device);
        protected abstract void OnDeviceConnected(BluetoothDevice device);
        protected abstract void OnDeviceDisconnecting(BluetoothDevice device);
        protected abstract void OnBondingRequiered(BluetoothDevice device);
        protected abstract void OnBonded(BluetoothDevice device);
        protected abstract void OnBatteryValueReceived(int value);
        protected abstract void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic);
        protected abstract void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic);
        protected abstract void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor);
        protected abstract void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor);

        protected abstract void
            OnCharacteristicNotified(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic);

        protected abstract void OnCharacteristicIndicated(BluetoothGatt gatt,
            BluetoothGattCharacteristic characteristic);

        protected abstract void OnError(BluetoothDevice device, Error errorType, GattStatus status);

        protected abstract void OnIndicationsEnabled(BluetoothGatt gatt,
            BluetoothGattCharacteristic descriptorCharacteristic);

        protected abstract void OnNotificationsEnabled(BluetoothGatt gatt,
            BluetoothGattCharacteristic descriptorCharacteristic);


        protected virtual bool IsRequiredServiceSupported(BluetoothGatt gatt)
        {
            return true;
        }

        protected virtual bool IsOptionalServiceSupported(BluetoothGatt gatt)
        {
            return true;
        }

        protected virtual bool ShouldEnableBatteryLevelNotifications(BluetoothDevice gattDevice)
        {
            return false;
        }

        #region Connect

        private bool _initialConnection = true;

        private readonly object _lock = new object();

        private BluetoothGatt _bluetoothGatt;

        private readonly Context _context;

        private readonly Handler _handler;
        
        private int _connectionKey;

        private readonly BroadcastReceiverEventDispatch _bluetoothStateBroadcastReceiver =
            new BroadcastReceiverEventDispatch();

        private readonly BroadcastReceiverEventDispatch
            _bondingBroadcastReceiver = new BroadcastReceiverEventDispatch();

        /// <summary>
        ///     This flag is set to false only when the {@link #shouldAutoConnect()} method returns true and the device got
        ///     disconnected without calling {@link #disconnect()} method.
        ///     If {@link #shouldAutoConnect()} returns false (default) this is always set to true.
        /// </summary>
        private bool _userDisconnected;

        private BluetoothDevice _bluetoothDevice;

        private BluetoothGattCallback _gattCallback;

        /// <summary>
        /// </summary>
        public bool ShouldAutoConnect { get; set; } = false;

        /// <summary>
        ///     Return wether we &are connected to a smart device or not
        /// </summary>
        public bool IsConnected { get; private set; }


        /// <summary>
        ///     Return the current connection state
        /// </summary>
        public State ConnectionState { get; private set; } = State.Disconnected;

        /**
         * Connects to the Bluetooth Smart device.
         *
         * @param device a device to connect to
         */
        public void ConnectDevice(BluetoothDevice device)
        {
            // TODO : Deal with the case "already connected" || connecting
            if (IsConnected) 
                return;

            lock (_lock)
            {
                if (ConnectionState != State.Disconnected) return;

                if (_bluetoothGatt != null)
                {
                    // There are 2 ways of reconnecting to the same device:
                    // 1. Reusing the same BluetoothGatt object and calling connect() - this will force the autoConnect flag to true
                    // 2. Closing it and reopening a new instance of BluetoothGatt object.
                    // The gatt.close() is an asynchronous method. It requires some time before it's finished and
                    // device.connectGatt(...) can't be called immediately or service discovery
                    // may never finish on some older devices (Nexus 4, Android 5.0.1).
                    // If shouldAutoConnect() method returned false we can't call gatt.connect() and have to close gatt and open it again.
                    if (!_initialConnection)
                    {
                        Log("closing gatt");
                        _bluetoothGatt.Close();
                        _bluetoothGatt = null;
                        Task.Delay(250).ContinueWith(t => ConnectDevice(device));
                    }
                    else
                    {
                        // Instead, the gatt.connect() method will be used to reconnect to the same device.
                        // This method forces autoConnect = true even if the gatt was created with this flag set to false.
                        _initialConnection = false;
                        Log("Connecting...");
                        ConnectionState = State.Connecting;
                        OnDeviceConnecting(device);
                        Log("gatt.connect()");
                        Log("gatt.connect()");
                        var rand2 = new Random().Next() + 1;
                        _connectionKey = rand2;
                        Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(t =>
                        {
                            if (_connectionKey == rand2)
                            {
                                OnConnectionStateChange(_bluetoothGatt, GattStatus.Failure, ProfileState.Disconnected);
                            }
                        });
                        
                        _bluetoothGatt.Connect();
                        return;
                    }
                }
                else
                {
                    Log("Registering cbrodcast listener");
                    // Register bonding broadcast receiver
                    _context.RegisterReceiver(_bluetoothStateBroadcastReceiver,
                        new IntentFilter(BluetoothAdapter.ActionStateChanged));
                    _context.RegisterReceiver(_bondingBroadcastReceiver,
                        new IntentFilter(BluetoothDevice.ActionBondStateChanged));
//					mContext.registerReceiver(mPairingRequestBroadcastReceiver, new IntentFilter("android.bluetooth.device.action.PAIRING_REQUEST"/*BluetoothDevice.ACTION_PAIRING_REQUEST*/));
                }
            }


            _userDisconnected =
                !ShouldAutoConnect; // We will receive Linkloss events only when the device is connected with autoConnect=true
            // The first connection will always be done with autoConnect = false to make the connection quick.
            // If the shouldAutoConnect() method returned true, the manager will automatically try to reconnect to this device on link loss.
            if (ShouldAutoConnect)
                _initialConnection = true;
            _bluetoothDevice = device;
            Log("Connecting...");
            ConnectionState = State.Connecting;
            OnDeviceConnecting(device);
            Log("gatt = device.connectGatt(autoConnect = false)");
            var rand = new Random().Next() + 1;
            _connectionKey = rand;
            Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(t =>
            {
                if (_connectionKey == rand)
                {
                    OnConnectionStateChange(_bluetoothGatt, GattStatus.Failure, ProfileState.Disconnected);
                }
            });
            
            _bluetoothGatt = device?.ConnectGatt(_context, false, _gattCallback = this);
        }


        /**
         * Disconnects from the device or cancels the pending connection attempt. Does nothing if device was not connected.
         *
         * @return true if device is to be disconnected. False if it was already disconnected.
         */
        public Error DisconnectDevice()
        {
            _userDisconnected = true;
            _initialConnection = false;

            if (_bluetoothGatt == null) return Error.PreconditionsFailed;

            ConnectionState = State.Disconnecting;
            Log(IsConnected ? "Disconnecting..." : "Cancelling connection...");
            OnDeviceDisconnecting(_bluetoothGatt.Device);
            var wasConnected = IsConnected;
            Log("gatt.disconnect()");
            _bluetoothGatt.Disconnect();

            if (wasConnected) return Error.Success;

            // There will be no callback, the connection attempt will be stopped
            ConnectionState = State.Disconnected;
            Log("Disconnected");
            OnDeviceDisconnected(_bluetoothGatt.Device);
            return Error.Success;
        }

        /**
   * Closes and releases resources. May be also used to unregister broadcast listeners.
   */
        private void Close()
        {
            try
            {
                _context.UnregisterReceiver(_bluetoothStateBroadcastReceiver);
                _context.UnregisterReceiver(_bondingBroadcastReceiver);
//			    mContext.unregisterReceiver(mPairingRequestBroadcastReceiver);
            }
            catch (Exception)
            {
                // the receiver must have been not registered or unregistered before
            }

            lock (_lock)
            {
                if (_bluetoothGatt != null)
                {
                    Log("gatt.close()");
                    _bluetoothGatt.Close();
                    _bluetoothGatt = null;
                }

                IsConnected = false;
                _initialConnection = false;
                ConnectionState = State.Disconnected;
                _gattCallback = null;
                _bluetoothDevice = null;
            }
        }

        private void NotifyDeviceDisconnected(BluetoothDevice device)
        {
            IsConnected = false;
            ConnectionState = State.Disconnected;
            if (_userDisconnected)
            {
                Log("Disconnected");
                Close();
            }
            OnDeviceDisconnected(device);
        }

        protected static string State2String(State state)
        {
            switch (state)
            {
                case State.TurningOn: return "Turning On";
                case State.On: return "On";
                case State.TurningOff: return "Turning Off";
                case State.Off: return "off";
                case State.Connected: return "Connected";
                case State.Connecting: return "Connecting";
                case State.Disconnected: return "Disconnected";
                case State.Disconnecting: return "Disconnecting";
                default:
                    return "Unknown (" + state + ")";
            }
        }

        #endregion

        #region Operation

        protected Error Write(BluetoothGattCharacteristic characteristic, byte[] value,
            GattWriteType writeType = GattWriteType.Default)
        {
            if (_bluetoothGatt == null || characteristic == null || value == null)
                return Error.PreconditionsFailed;

            // Check characteristic property
            var properties = characteristic.Properties;
            if ((properties & (GattProperty.Write | GattProperty.WriteNoResponse)) == 0)
                return Error.Unsupported;

            characteristic.SetValue(value);
            characteristic.WriteType = writeType;
            var success = _bluetoothGatt.WriteCharacteristic(characteristic);
            return success ? Error.Success : Error.Failed;
        }

        protected Error Read(BluetoothGattCharacteristic characteristic)
        {
            if (_bluetoothGatt == null || characteristic == null)
                return Error.PreconditionsFailed;

            // Check characteristic property
            if ((characteristic.Properties & GattProperty.Read) == 0)
                return Error.Unsupported;

//            Logger?.v("Reading characteristic " + characteristic.Uuid);
//            Logger?.v("gatt.readCharacteristic(" + characteristic.Uuid + ")");
            return _bluetoothGatt.ReadCharacteristic(characteristic) ? Error.Success : Error.Failed;
        }

        protected Error EnableIndications(BluetoothGattCharacteristic characteristic)
        {
            /*
            descriptor.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
            _bluetoothGatt.WriteDescriptor(descriptor);
             */

            var gatt = _bluetoothGatt;
            if (gatt == null || characteristic == null)
                return Error.PreconditionsFailed;

            // Check characteristic property
            var properties = characteristic.Properties;
            if ((properties & GattProperty.Indicate) == 0)
                return Error.Unsupported;

//            Logger?.v("gatt.setCharacteristicNotification(" + characteristic.Uuid + ", true)");
            gatt.SetCharacteristicNotification(characteristic, true);
            var descriptor = characteristic.GetDescriptor(KnownUUID.CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID);
            if (descriptor != null)
                //                descriptor.SetValue(BluetoothGattDescriptor.EnableIndicationValue.ToArray());
//                Logger?.v("Enabling indications for " + characteristic.Uuid);
//                Logger?.v("gatt.writeDescriptor(" + KnownUUID.CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID +
//                          ", value=0x02-00)");
                return WriteDescriptorWorkaround(descriptor);

            return Error.Unsupported;
        }

        protected Error EnableNotifications(BluetoothGattCharacteristic characteristic)
        {
            if (_bluetoothGatt == null || characteristic == null)
                return Error.PreconditionsFailed;

            // Check characteristic property
            if ((characteristic.Properties & GattProperty.Notify) == 0)
                return Error.Unsupported;

            _bluetoothGatt.SetCharacteristicNotification(characteristic, true);
            var descriptor = characteristic.GetDescriptor(KnownUUID.CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID);
            if (descriptor != null)
            {
                descriptor.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                return WriteDescriptorWorkaround(descriptor);
            }

            return Error.Unsupported;
        }

        protected Error WriteDescriptorWorkaround(BluetoothGattDescriptor descriptor)
        {
            var gatt = _bluetoothGatt;
            if (gatt == null || descriptor == null)
                return Error.PreconditionsFailed;

            var parentCharacteristic = descriptor.Characteristic;
            var originalWriteType = parentCharacteristic.WriteType;
            parentCharacteristic.WriteType = GattWriteType.Default;
            var result = gatt.WriteDescriptor(descriptor);
            parentCharacteristic.WriteType = originalWriteType;
            return result ? Error.Success : Error.Failed;
        }

        #endregion

        #region Helper

        /**
 * Returns true if this descriptor is from the Service Changed characteristic.
 *
 * @param descriptor the descriptor to be checked
 * @return true if the descriptor belongs to the Service Changed characteristic
 */
        private bool IsServiceChangedCCCD(BluetoothGattDescriptor descriptor)
        {
            return descriptor != null &&
                   KnownUUID.SERVICE_CHANGED_CHARACTERISTIC.Equals(descriptor.Characteristic.Uuid);
        }

        /**
         * Returns true if the characteristic is the Battery Level characteristic.
         *
         * @param characteristic the characteristic to be checked
         * @return true if the characteristic is the Battery Level characteristic.
         */
        private bool IsBatteryLevelCharacteristic(BluetoothGattCharacteristic characteristic)
        {
            return characteristic != null && KnownUUID.BATTERY_LEVEL_CHARACTERISTIC.Equals(characteristic.Uuid);
        }

        /**
         * Returns true if this descriptor is from the Battery Level characteristic.
         *
         * @param descriptor the descriptor to be checked
         * @return true if the descriptor belongs to the Battery Level characteristic
         */
        private bool IsBatteryLevelCCCD(BluetoothGattDescriptor descriptor)
        {
            return descriptor != null && KnownUUID.BATTERY_LEVEL_CHARACTERISTIC.Equals(descriptor.Characteristic.Uuid);
        }

        /**
         * Returns true if this descriptor is a Client Characteristic Configuration descriptor (CCCD).
         *
         * @param descriptor the descriptor to be checked
         * @return true if the descriptor is a CCCD
         */
        private bool IsCCCD(BluetoothGattDescriptor descriptor)
        {
            return descriptor != null && KnownUUID.CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID.Equals(descriptor.Uuid);
        }

        #endregion

        #region Broadcast Received

        private void OnBluetoothStateBroadcast(object sender, Intent intent)
        {
            var state = (State) intent.GetIntExtra(BluetoothAdapter.ExtraState, (int) State.Off);
            var previousState = (State) intent.GetIntExtra(BluetoothAdapter.ExtraPreviousState, (int) State.Off);

            switch (state)
            {
                case State.TurningOff:
                case State.Off:
                    if (IsConnected && previousState != State.TurningOff && previousState != State.Off)
                        NotifyDeviceDisconnected(_bluetoothDevice);
                    // Calling close() will prevent the STATE_OFF event from being logged (this receiver will be unregistered). But it doesn't matter.
                    Close();
                    break;
            }
        }


        private void OnBondingBroadcastReceived(object sender, Intent intent)
        {
//		    var device = (BluetoothDevice) intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
//		    var bondState = (Bond) intent.GetIntExtra(BluetoothDevice.ExtraBondState, -1);
//		    var previousBondState = (Bond) intent.GetIntExtra(BluetoothDevice.ExtraPreviousBondState, -1);
//
//		    // Skip other devices
//		    if (_bluetoothGatt == null || !device.Address.Equals(_bluetoothGatt.Device.Address))
//			    return;
//
//		    Logger?.v("[Broadcast] Action received: " + BluetoothDevice.ActionBondStateChanged + ", bond state changed to: " + bondStateToString(bondState) + " (" + bondState + ")");
//		    Log.i(TAG, "Bond state changed for: " + device.getName() + " new state: " + bondState + " previous: " + previousBondState);
//
//		    switch (bondState) {
//			    case Bond.Bonding:
//				    OnBondingRequiered(device);
//				    break;
//			    case Bond.Bonded:
////				    Logger.i("Device bonded");
//				    OnBonded(device);
//				    // If the device started to pair just after the connection was established the services were not discovered.
//				    if (_bluetoothGatt.Services.Count == 0) {
//					    _handler.Post(() => {
////						    Logger?.v("Discovering Services...");
////						    Logger?.v("gatt.discoverServices()");
//						    _bluetoothGatt.DiscoverServices();
//					    });
//				    }
//				    break;
//		    }
        }

        #endregion

        #region BluetoothGattCallback

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (status == GattStatus.Success && newState == ProfileState.Connected)
            {
                // Notify the parent activity/service
                Log("Connected to " + gatt.Device.Address);
                IsConnected = true;
                ConnectionState = State.Connected;
                OnDeviceConnected(gatt.Device);

                /*
                 * The onConnectionStateChange event is triggered just after the Android connects to a device.
                 * In case of bonded devices, the encryption is reestablished AFTER this callback is called.
                 * Moreover, when the device has Service Changed indication enabled, and the list of services has changed (e.g. using the DFU),
                 * the indication is received few hundred milliseconds later, depending on the connection interval.
                 * When received, Android will start performing a service discovery operation on its own, internally,
                 * and will NOT notify the app that services has changed.
                 *
                 * If the gatt.discoverServices() method would be invoked here with no delay, if would return cached services,
                 * as the SC indication wouldn't be received yet.
                 * Therefore we have to postpone the service discovery operation until we are (almost, as there is no such callback) sure,
                 * that it has been handled.
                 * TODO: Please calculate the proper delay that will work in your solution.
                 * It should be greater than the time from LLCP Feature Exchange to ATT Write for Service Change indication.
                 * If your device does not use Service Change indication (for example does not have DFU) the delay may be 0.
                 */
                var bonded = gatt.Device.BondState == Bond.Bonded;

                var delay = bonded ? 1600 : 45; // around 1600 ms is required when connection interval is ~45ms.
//				if (delay > 0)
//                Logger?.v("wait(" + delay + ")");
                _handler.PostDelayed(() =>
                {
                    // Some proximity tags (e.g. nRF PROXIMITY) initialize bonding automatically when connected.
                    if (gatt.Device.BondState != Bond.Bonding)
                        //                        Logger?.v("Discovering service");
                        gatt.DiscoverServices();
                }, delay);
            }
            else
            {
                Log("Change d");
                
                if (newState == ProfileState.Disconnected)
                {
//					
                    var wasConnected = IsConnected;
                    // if (mConnected) { // Checking mConnected prevents from calling onDeviceDisconnected if connection attempt failed. This check is not necessary
                    if (gatt != null)
                    {
                        NotifyDeviceDisconnected(gatt.Device); // This sets the mConnected flag to false
                        if (_initialConnection) ConnectDevice(gatt.Device);

                        if (wasConnected || status == GattStatus.Success)
                            return;
                    }
                }

                OnError(gatt?.Device, Error.LinkLost, status);
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            _connectionKey = 0;
            
            if (status == GattStatus.Success)
            {
//                Logger?.v("Services Discovered");
                if (IsRequiredServiceSupported(gatt))
                    //                    Logger?.v("Primary service found");

                    // Notify the parent activity
                    OnServiceDiscovered(gatt);
                else
                    //                    Logger?.v("Device is not supported");
                    DisconnectDevice();
            }
            else
            {
                Trace.WriteLine("onServicesDiscovered error " + status);
                OnError(gatt.Device, Error.DiscoveryService, status);
            }
        }

        public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic,
            GattStatus status)
        {
            if (status == GattStatus.Success)
            {
                //if (IsBatteryLevelCharacteristic(characteristic))
                //{
                //    var batteryValue = characteristic.GetIntValue(BluetoothGattCharacteristic.FormatUint8, 0);
                //    _batteryValue = batteryValue.IntValue();
                //    OnBatteryValueReceived(_batteryValue);
                //}
                //else
                //{
                // The value has been read. Notify the manager and proceed with the initialization queue.
                OnCharacteristicRead(gatt, characteristic);
                //}
            }
            else if (status == GattStatus.InsufficientAuthentication)
            {
                if (gatt.Device.BondState != Bond.None) OnError(gatt.Device, Error.AuthErrorWhileBonded, status);
            }
            else
            {
                OnError(gatt.Device, Error.Unknown, status);
            }
        }


        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic,
            GattStatus status)
        {
            if (status == GattStatus.Success)
            {
//			    Logger?.i("Data written to " + characteristic.getUuid() + ", value: " + ParserUtils.parse(characteristic));
                // The value has been written. Notify the manager and proceed with the initialization queue.
                OnCharacteristicWrite(gatt, characteristic);
            }
            else if (status == GattStatus.InsufficientAuthentication)
            {
                if (gatt.Device.BondState != Bond.None) OnError(gatt.Device, Error.AuthErrorWhileBonded, status);
            }
            else
            {
//			    Log.e(TAG, "onCharacteristicWrite error " + status);
                OnError(gatt.Device, Error.Unknown, status);
            }
        }

        public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor,
            GattStatus status)
        {
            if (status == GattStatus.Success)
            {
//			    Logger?.i("Data written to descr. " + descriptor.getUuid() + ", value: " + ParserUtils.parse(descriptor));

                if (IsServiceChangedCCCD(descriptor))
                {
//				    Logger?.a("Service Changed notifications enabled");
                }
                else if (IsBatteryLevelCCCD(descriptor))
                {
                    var value = descriptor.GetValue();
                    if (value != null && value.Length == 2 && value[1] == 0x00)
                    {
                        if (value[0] == 0x01)
                        {
//						    Logger?.a("Battery Level notifications enabled");
                        }
                    }
                    else
                    {
                        OnDescriptorWrite(gatt, descriptor);
                    }
                }
                else if (IsCCCD(descriptor))
                {
                    var value = descriptor.GetValue();
                    if (value != null && value.Length == 2 && value[1] == 0x00)
                        switch (value[0])
                        {
                            case 0x00:
                                OnNotificationsEnabled(gatt, descriptor.Characteristic);
                                OnIndicationsEnabled(gatt, descriptor.Characteristic);
                                break;
                            case 0x01:
                                OnNotificationsEnabled(gatt, descriptor.Characteristic);
                                break;
                            case 0x02:
                                OnIndicationsEnabled(gatt, descriptor.Characteristic);
                                break;
                        }
                    else
                        OnDescriptorWrite(gatt, descriptor);
                }
                else
                {
                    OnDescriptorWrite(gatt, descriptor);
                }
            }
            else if (status == GattStatus.InsufficientAuthentication)
            {
                if (gatt.Device.BondState != Bond.None) OnError(gatt.Device, Error.AuthErrorWhileBonded, status);
            }
            else
            {
//			    Log.e(TAG, "onDescriptorWrite error " + status);
                OnError(gatt.Device, Error.Unknown, status);
            }
        }

        public override void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
        {
            switch (status)
            {
                case GattStatus.Success:
                    // The value has been read. Notify the manager and proceed with the initialization queue.
                    OnDescriptorRead(gatt, descriptor);
                    break;
                case GattStatus.InsufficientAuthentication:
                    if (gatt.Device.BondState != Bond.None) OnError(gatt.Device, Error.AuthErrorWhileBonded, status);

                    break;
                default:
                    OnError(gatt.Device, Error.Unknown, status);
                    break;
            }
        }

        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            if (IsBatteryLevelCharacteristic(characteristic))
            {
                var batteryValue = characteristic.GetIntValue(GattFormat.Uint8, 0);
                _batteryValue = batteryValue.IntValue();
                OnBatteryValueReceived(_batteryValue);
            }
            else
            {
                var cccd = characteristic.GetDescriptor(KnownUUID.CLIENT_CHARACTERISTIC_CONFIG_DESCRIPTOR_UUID);
                var notifications = cccd == null || cccd.GetValue() == null || cccd.GetValue().Length != 2 ||
                                    cccd.GetValue()[0] == 0x01;

                if (notifications)
                    OnCharacteristicNotified(gatt, characteristic);
                else
                    OnCharacteristicIndicated(gatt, characteristic);
            }
        }

        #endregion

        private void Log(string txt)
        {
#if DEBUG

            Android.Util.Log.Debug(this.Class.Name, txt);

#endif
        }
    }

    
}