using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Borea.Core.Secrets;

namespace Borea.Storage.Secrets;

/// <summary>
/// ISecretStore through the login Keychain of the current user, as generic passwords of the service "Borea".
/// It calls Security.framework directly, because the security command would put the secret into a process argument.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class KeychainSecretStore : ISecretStore
{
    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Service = "Borea";
    private const uint Utf8Encoding = 0x08000100;

    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int NotAvailable = -25291;
    private const int NoSuchKeychain = -25294;

    private static readonly Lazy<Symbols?> Loaded = new(Symbols.Load);

    public string? Read(string name)
    {
        var symbols = Require();
        return Call(name, account =>
        {
            var query = PasswordQuery(symbols, account, (symbols.ReturnData, symbols.True), (symbols.MatchLimit, symbols.MatchLimitOne));
            try
            {
                var status = SecItemCopyMatching(query, out var data);
                if (status == ItemNotFound)
                    return null;

                ThrowIfFailed(status);
                try
                {
                    var bytes = new byte[(int)CFDataGetLength(data)];
                    Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
                    try
                    {
                        return Encoding.UTF8.GetString(bytes);
                    }
                    finally
                    {
                        Array.Clear(bytes);
                    }
                }
                finally
                {
                    Release(data);
                }
            }
            finally
            {
                Release(query);
            }
        });
    }

    public void Write(string name, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var symbols = Require();
        Call<object?>(name, account =>
        {
            var plain = Encoding.UTF8.GetBytes(secret);
            var data = CFDataCreate(IntPtr.Zero, plain, plain.Length);
            Array.Clear(plain);
            var query = PasswordQuery(symbols, account);
            var change = CFDictionaryCreate(IntPtr.Zero, [symbols.ValueData], [data], 1, symbols.KeyCallBacks, symbols.ValueCallBacks);
            var added = PasswordQuery(symbols, account, (symbols.ValueData, data));
            try
            {
                var status = SecItemUpdate(query, change);
                if (status == ItemNotFound)
                    status = SecItemAdd(added, IntPtr.Zero);

                ThrowIfFailed(status);
                return null;
            }
            finally
            {
                Release(added);
                Release(change);
                Release(query);
                Release(data);
            }
        });
    }

    public void Delete(string name)
    {
        var symbols = Require();
        Call<object?>(name, account =>
        {
            var query = PasswordQuery(symbols, account);
            try
            {
                var status = SecItemDelete(query);
                if (status != ItemNotFound)
                    ThrowIfFailed(status);

                return null;
            }
            finally
            {
                Release(query);
            }
        });
    }

    private static Symbols Require() =>
        Loaded.Value ?? throw new SecretStoreException(SecretStoreProblem.Missing, "The Keychain of macOS cannot be loaded.");

    private static T Call<T>(string name, Func<IntPtr, T> work)
    {
        SecretNames.Validate(name);
        try
        {
            var account = CFStringCreateWithCString(IntPtr.Zero, name, Utf8Encoding);
            try
            {
                return work(account);
            }
            finally
            {
                Release(account);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException(SecretStoreProblem.Missing, "The Keychain of macOS cannot be loaded.", exception);
        }
    }

    /// <summary>The query for the generic password <paramref name="account"/> of Borea, with <paramref name="extra"/> added.</summary>
    private static IntPtr PasswordQuery(Symbols symbols, IntPtr account, params (IntPtr Key, IntPtr Value)[] extra)
    {
        var service = CFStringCreateWithCString(IntPtr.Zero, Service, Utf8Encoding);
        try
        {
            IntPtr[] keys = [symbols.Class, symbols.AttrService, symbols.AttrAccount, .. extra.Select(pair => pair.Key)];
            IntPtr[] values = [symbols.ClassGenericPassword, service, account, .. extra.Select(pair => pair.Value)];
            return CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, symbols.KeyCallBacks, symbols.ValueCallBacks);
        }
        finally
        {
            Release(service);
        }
    }

    // CFRelease stops the process on null.
    private static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
            CFRelease(value);
    }

    private static void ThrowIfFailed(int status)
    {
        if (status == Success)
            return;

        var problem = status is NotAvailable or NoSuchKeychain ? SecretStoreProblem.Missing : SecretStoreProblem.Refused;
        throw new SecretStoreException(problem, $"The Keychain answered {status}.");
    }

    private sealed record Symbols(
        IntPtr Class,
        IntPtr ClassGenericPassword,
        IntPtr AttrService,
        IntPtr AttrAccount,
        IntPtr ValueData,
        IntPtr ReturnData,
        IntPtr MatchLimit,
        IntPtr MatchLimitOne,
        IntPtr True,
        IntPtr KeyCallBacks,
        IntPtr ValueCallBacks)
    {
        public static Symbols? Load()
        {
            if (!NativeLibrary.TryLoad(SecurityLibrary, out var security) || !NativeLibrary.TryLoad(CoreFoundationLibrary, out var coreFoundation))
                return null;

            try
            {
                IntPtr Constant(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

                return new Symbols(
                    Constant(security, "kSecClass"),
                    Constant(security, "kSecClassGenericPassword"),
                    Constant(security, "kSecAttrService"),
                    Constant(security, "kSecAttrAccount"),
                    Constant(security, "kSecValueData"),
                    Constant(security, "kSecReturnData"),
                    Constant(security, "kSecMatchLimit"),
                    Constant(security, "kSecMatchLimitOne"),
                    Constant(coreFoundation, "kCFBooleanTrue"),
                    NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                    NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"));
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }
    }

    [DllImport(SecurityLibrary)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr value);
}
