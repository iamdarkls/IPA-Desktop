using System;
using System.Security;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
namespace IpaDesktop;
/// <summary>Bounded UTF-8 input used by both terminal implementations, wiped on disposal.</summary>
public sealed class LoginInput : IDisposable
{
    readonly byte[] bytes;
    public ReadOnlyMemory<byte> Bytes => bytes;
    public LoginInput(SecureString value,char? terminator)
    {
        if(value==null||value.Length==0||value.Length>1024)throw new ArgumentException("Проверьте введённые данные.");
        IntPtr pointer=IntPtr.Zero;char[] chars=null;
        try
        {
            pointer=Marshal.SecureStringToGlobalAllocUnicode(value);chars=new char[value.Length+(terminator.HasValue?1:0)];
            for(int i=0;i<value.Length;i++)
            {
                chars[i]=(char)Marshal.ReadInt16(pointer,i*2);
                if(Char.IsControl(chars[i]))throw new ArgumentException("Ввод не должен содержать управляющие символы или переносы строк.");
            }
            if(terminator.HasValue)chars[value.Length]=terminator.Value;
            bytes=Encoding.UTF8.GetBytes(chars);
        }
        finally {if(pointer!=IntPtr.Zero)Marshal.ZeroFreeGlobalAllocUnicode(pointer);if(chars!=null)Array.Clear(chars);}
    }
    public void Dispose()=>CryptographicOperations.ZeroMemory(bytes);
}
