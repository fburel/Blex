namespace Blex
{
    public interface IEncryptionHandler
    {
        /// <summary>
        /// Implement this method to encrypt the given secret for the given characteristic uuid
        /// </summary>
        /// <param name="secret"></param>
        /// <param name="characterisitic"></param>
        /// <returns></returns>
        byte[] EncryptMessage(byte[] secret, string characterisitic);
        
        /// <summary>
        /// Implement this method to decrypt the given secret received from given characteristic uuid
        /// </summary>
        /// <param name="secret"></param>
        /// <param name="characterisitic"></param>
        /// <returns></returns>
        byte[] DecryptMessage(byte[] secret, string characterisitic);
    }
}