// ITitleKeyProvider.cs
//
// Runtime seam for supplying Wii U decryption keys. Implementations live host-side and are
// user-configured; NO key material is embedded in this module or anywhere in the repository.
//
// Spec references (clean-room): wiiubrew "Encryption keys" (the common key), wiibrew "Ticket"
// (the per-title encrypted title key). No GPL source was consulted.

namespace Module.WiiUTools;

/// <summary>
/// Supplies the key material needed to decrypt Wii U titles at runtime. The common key
/// decrypts a ticket's encrypted title key (see <see cref="WiiUCrypto.DecryptTitleKey"/>);
/// alternatively a host may supply an already-decrypted per-title key directly (e.g. from an
/// imported title-key database), bypassing the ticket.
///
/// Implementations are host-side and user-configured. This module ships no keys, so that the
/// download path (which lands encrypted bytes) works with no keys present and only decryption
/// requires the user to provide them.
/// </summary>
public interface ITitleKeyProvider
{
    /// <summary>
    /// The 16-byte Wii U common key used to decrypt ticket title keys, or <c>null</c> if the
    /// user has not configured it (decryption is then unavailable — the "keys required" state).
    /// </summary>
    byte[]? GetCommonKey();

    /// <summary>
    /// An already-decrypted 16-byte title key for <paramref name="titleId"/>, or <c>null</c> to
    /// indicate the key should be derived from the title's ticket using the common key.
    /// <paramref name="titleId"/> is the 16-hex-character title ID (e.g. <c>0005000010123400</c>).
    /// </summary>
    byte[]? GetTitleKey(string titleId);
}
