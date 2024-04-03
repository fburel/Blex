using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Blex
{
    public delegate void Characteristicupdated(string characteristic);

    public interface IBluetoothHandler
    {

        /// <summary>
        /// Implement this delegate if you choose to receive log from this library
        /// </summary>
        event EventHandler<string> LogReceived;
            
        /// <summary>
        /// This method scan for BLE device
        /// </summary>
        /// <param name="limit"> the maximum number of result desired. OOnce this number is reached, the scan stops and return</param>
        /// <param name="maxDuration">The maximum time allowed for the scan. If the maximum number of result desired hasn't been met after this delay is passse, the scan stops.</param>
        /// <param name="filter"> A predicate allowing to ignore some result, for example you can specifies a RSSI limit.</param>
        /// <param name="uuidsRequiered"> A list of Gatt Service UIID. Device not advertising those services will be ignored</param>
        /// <returns></returns>
        Task<IEnumerable<IScanResult>> Scan(int limit, TimeSpan maxDuration, Predicate<IScanResult> filter,
            string[] uuidsRequiered);

        [Obsolete("use Scan(int, int, Predicate<IScanResult>, string[]) instead", false)]
        Task<IEnumerable<IScanResult>> Scan(int limit, int maxDuration, int RSSILimit, string[] uuidsRequiered);
        
        #region Connection

        /// <summary>
        /// A boolean value indicating if an active BLE connection currently exist
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Connect to a given device
        /// </summary>
        /// <param name="scanResult"> The device to connect to</param>
        /// <returns></returns>
        Task Connect(IScanResult scanResult);

        /// <summary>
        /// Disconnect for the currently connected device
        /// </summary>
        /// <returns></returns>
        Task Disconnect();

        /// <summary>
        /// An event raised when the connection status changed.
        /// </summary>
        /// <note>
        /// As observed when the bluetooth device disconnect itself:
        /// on iOS the event is raised instantly
        /// on event is raise after a delay up to 20 seconds 
        /// </note>
        event EventHandler<bool> ConnectionStatusChanged;

        #endregion

        #region GATT Discovery

        /// <summary>
        /// This methods discovers the GATT services and characteristic of the connected device
        /// </summary>
        /// <returns></returns>
        Task DiscoverGATT();

        /// <summary>
        /// Return wether a given characteristic has been discovered
        /// </summary>
        /// <param name="characteristic"></param>
        /// <returns></returns>
        bool HasDiscoveredCharacteristics(string characteristic);
        
        #endregion
        
        #region Encryption
        
        /// <summary>
        /// Define an encryption delegate instance.
        /// If not null, all message being written will be encrypted using the EncryptMessage method before being written
        /// Also all messages or notificatiosn read will be decrypted beforehand using the DecryptMessage methods
        /// </summary>
        IEncryptionHandler EncryptionHandler { get; set; }
        
        #endregion

        #region Writting

        Task WriteValue(byte[] value, string characteristic);

        void WriteWithoutResponse(byte[] value, string characteristic);

        #endregion

        #region Reading

        Task<byte[]> ReadData(string characteristic);

        byte[] ArchivedValue(string characteristic);

        #endregion

        #region Notifications

        Task Subscribe(string characteristic, Characteristicupdated @object);

        void Unsubscribe(Characteristicupdated @object);


        #endregion

    

    }
}