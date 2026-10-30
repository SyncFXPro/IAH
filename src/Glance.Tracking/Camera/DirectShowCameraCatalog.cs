using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Glance.Core.Contracts;

namespace Glance.Tracking.Camera;

/// <summary>
/// DirectShow video-input devices, in the same order OpenCV opens with CAP_DSHOW.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DirectShowCameraCatalog : ICameraCatalog
{
    public IReadOnlyList<CameraDescriptor> List()
    {
        var found = new List<CameraDescriptor>();
        CreateDevEnum? enumerator = null;
        IEnumMoniker? monikers = null;
        try
        {
            enumerator = new CreateDevEnum();
            var category = VideoInputDeviceCategory;
            var hr = ((ICreateDevEnum)enumerator).CreateClassEnumerator(ref category, out monikers, 0);
            if (hr != 0 || monikers is null)
            {
                return found;
            }

            var fetched = Marshal.AllocCoTaskMem(IntPtr.Size);
            try
            {
                var slot = new IMoniker[1];
                var index = 0;
                while (monikers.Next(1, slot, fetched) == 0 && slot[0] is not null)
                {
                    var moniker = slot[0];
                    try
                    {
                        var name = ReadFriendlyName(moniker);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = "Camera " + index.ToString();
                        }

                        found.Add(new CameraDescriptor(index.ToString() + "|" + name, name, index));
                        index++;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(moniker);
                        slot[0] = null!;
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(fetched);
            }
        }
        catch (COMException)
        {
            return found;
        }
        catch (InvalidCastException)
        {
            return found;
        }
        finally
        {
            if (monikers is not null)
            {
                Marshal.ReleaseComObject(monikers);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        return found;
    }

    private static string? ReadFriendlyName(IMoniker moniker)
    {
        var bagId = typeof(IPropertyBag).GUID;
        moniker.BindToStorage(null!, null!, ref bagId, out var bag);
        if (bag is not IPropertyBag propertyBag)
        {
            return null;
        }

        try
        {
            object value = string.Empty;
            var hr = propertyBag.Read("FriendlyName", ref value, IntPtr.Zero);
            return hr == 0 ? value as string : null;
        }
        finally
        {
            Marshal.ReleaseComObject(propertyBag);
        }
    }

    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    [ComImport]
    [Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
    private class CreateDevEnum
    {
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid deviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string name, ref object value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string name, ref object? value);
    }
}
