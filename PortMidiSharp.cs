// FIXMEs:
// - some bad mappings:
//	- C int -> C# int
//	- C long -> C# long
// not sure what they should be.
// The sources are wrong. Those C code should not use int and long for each.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using PmDeviceID = System.Int32;
using PmTimestamp = System.Int64;
using PortMidiStream = System.IntPtr;
using PmMessage = System.Int64;
using PmError = PortMidiSharp.MidiErrorType;

namespace PortMidiSharp
{
	public class MidiDeviceManager
	{
		static MidiDeviceManager ()
		{
			PortMidiMarshal.Pm_Initialize ();
			AppDomain.CurrentDomain.DomainUnload += delegate (object o, EventArgs e) {
				PortMidiMarshal.Pm_Terminate ();
			};
		}

		public static int DeviceCount {
			get { return PortMidiMarshal.Pm_CountDevices (); }
		}

		public static int DefaultInputDeviceID {
			get { return PortMidiMarshal.Pm_GetDefaultInputDeviceID (); }
		}

		public static int DefaultOutputDeviceID {
			get { return PortMidiMarshal.Pm_GetDefaultOutputDeviceID (); }
		}

		public static IEnumerable<MidiDeviceInfo> AllDevices {
			get {
				for (int i = 0; i < DeviceCount; i++)
					yield return GetDeviceInfo (i);
			}
		}

		public static MidiDeviceInfo GetDeviceInfo (PmDeviceID id)
		{
			return new MidiDeviceInfo (PortMidiMarshal.Pm_GetDeviceInfo (id));
		}

		public static MidiOutput OpenOutput (PmDeviceID outputDevice)
		{
			PortMidiStream stream;
			var e = PortMidiMarshal.Pm_OpenOutput (out stream, outputDevice, IntPtr.Zero, 0, null, IntPtr.Zero, 0);
			if (e != PmError.NoError)
				throw new MidiException (e, "Failed to open output device {0}");
			return new MidiOutput (stream, outputDevice, 0);
		}
	}

	public enum MidiErrorType
	{
		NoError = 0,
		NoData = 0,
		GotData = 1,
		HostError = -10000,
		InvalidDeviceId,
		InsufficientMemory,
		BufferTooSmall,
		BufferOverflow,
		BadPointer,
		BadData,
		InternalError,
		BufferMaxSize,
	}

	public class MidiException : Exception
	{
		PmError error_type;

		public MidiException (PmError errorType, string message)
			: this (errorType, message, null)
		{
		}

		public MidiException (PmError errorType, string message, Exception innerException)
			: base (message, innerException)
		{
			error_type = errorType;
		}

		public PmError ErrorType {
			get { return error_type; }
		}
	}

	public struct MidiDeviceInfo
	{
		PmDeviceInfo info;

		internal MidiDeviceInfo (PmDeviceInfo info)
		{
			this.info = info;
		}

		public string Interface {
			get { return Marshal.PtrToStringAnsi (info.Interface); }
		}

		public string Name {
			get { return Marshal.PtrToStringAnsi (info.Name); }
		}

		public bool IsInput { get { return info.Input != 0; } }
		public bool IsOutput { get { return info.Output != 0; } }
		public bool IsOpen { get { return info.Opened != 0; } }
	}

	public abstract class MidiStream : IDisposable
	{
		internal PortMidiStream stream;
		internal PmDeviceID device;

		protected MidiStream (PortMidiStream stream, PmDeviceID deviceID)
		{
			this.stream = stream;
			device = deviceID;
		}

		public void Abort ()
		{
			PortMidiMarshal.Pm_Abort (stream);
		}

		public void Close ()
		{
			Dispose ();
		}

		public void Dispose ()
		{
			PortMidiMarshal.Pm_Close (stream);
		}

		public void SetFilter (MidiFilter filters)
		{
			PortMidiMarshal.Pm_SetFilter (stream, filters);
		}

		public void SetChannelMask (int mask)
		{
			PortMidiMarshal.Pm_SetChannelMask (stream, mask);
		}
	}

	public class MidiInput : MidiStream
	{
		public MidiInput (PortMidiStream stream, PmDeviceID inputDevice)
			: base (stream, inputDevice)
		{
		}

		public int Read (MidiEvent [] buffer, long length)
		{
			return PortMidiMarshal.Pm_Read (stream, buffer, length);
		}
	}

	public class MidiOutput : MidiStream
	{
		public MidiOutput (PortMidiStream stream, PmDeviceID outputDevice, long latency)
			: base (stream, outputDevice)
		{
		}

		public void Write (MidiEvent mevent)
		{
			Write (mevent.Timestamp, mevent.Message);
		}

		public void Write (PmTimestamp when, MidiMessage msg)
		{
			PortMidiMarshal.Pm_WriteShort (stream, when, msg);
		}

		public void WriteSysEx (PmTimestamp when, byte [] sysex)
		{
			PortMidiMarshal.Pm_WriteSysEx (stream, when, sysex);
		}

		public void Write (MidiEvent [] buffer)
		{
			Write (buffer, 0, buffer.Length);
		}

		public void Write (MidiEvent [] buffer, int index, int length)
		{
			var gch = GCHandle.Alloc (buffer);
			try {
				var ptr = Marshal.UnsafeAddrOfPinnedArrayElement (buffer, index);
				PortMidiMarshal.Pm_Write (stream, ptr, length);
			} finally {
				gch.Free ();
			}
		}
	}

	[StructLayout (LayoutKind.Sequential)]
	public struct MidiEvent
	{
		MidiMessage msg;
		PmTimestamp ts;

		public MidiMessage Message {
			get { return msg; }
			set { msg = value; }
		}

		public PmTimestamp Timestamp {
			get { return ts; }
			set { ts = value; }
		}
	}

	public struct MidiMessage
	{
		PmMessage v;

		public MidiMessage (long status, long data1, long data2)
		{
			v = ((((data2) << 16) & 0xFF0000) | (((data1) << 8) & 0xFF00) | ((status) & 0xFF)); 
		}

		public PmMessage Value {
			get { return v; }
		}
	}

	public delegate PmTimestamp MidiTimeProcDelegate (IntPtr timeInfo);

	[Flags]
	public enum MidiFilter : long
	{
		Active = 1 << 0x0E,
		SysEx = 1 << 0x00,
		Clock = 1 << 0x08,
		Play = ((1 << 0x0A) | (1 << 0x0C) | (1 << 0x0B)),
		Tick = (1 << 0x09),
		FD = (1 << 0x0D),
		Undefined = FD,
		Reset = (1 << 0x0F),
		RealTime = (Active | SysEx | Clock | Play | Undefined | Reset | Tick),
		Note = ((1 << 0x19) | (1 << 0x18)),
		CAF = (1 << 0x1D),
		PAF = (1 << 0x1A),
		AF = (CAF | PAF),
		Program = (1 << 0x1C),
		Control = (1 << 0x1B),
		PitchBend = (1 << 0x1E),
		MTC = (1 << 0x01),
		SongPosition = (1 << 0x02),
		SongSelect = (1 << 0x03),
		Tune = (1 << 0x06),
		SystemCommon = (MTC | SongPosition | SongSelect | Tune)
	}

	// Marshal types

	class PortMidiMarshal
	{
		[DllImport ("portmidi")]
		public static extern PmError Pm_Initialize ();

		[DllImport ("portmidi")]
		public static extern PmError Pm_Terminate ();

		// TODO
		[DllImport ("portmidi")]
		static extern int Pm_HasHostError (PortMidiStream stream);

		// TODO
		[DllImport ("portmidi")]
		static extern string Pm_GetErrorText (PmError errnum);

		// TODO
		[DllImport ("portmidi")]
		static extern void Pm_GetHostErrorText (IntPtr msg, uint len);

		const int HDRLENGTH = 50;
		const uint PM_HOST_ERROR_MSG_LEN = 256;

		// Device enumeration

		const PmDeviceID PmNoDevice = -1;

		[DllImport ("portmidi")]
		public static extern int Pm_CountDevices ();

		[DllImport ("portmidi")]
		public static extern PmDeviceID Pm_GetDefaultInputDeviceID ();

		[DllImport ("portmidi")]
		public static extern PmDeviceID Pm_GetDefaultOutputDeviceID ();

		[DllImport ("portmidi")]
		public static extern PmDeviceInfo Pm_GetDeviceInfo (PmDeviceID id);

		[DllImport ("portmidi")]
		public static extern PmError Pm_OpenInput (
			out PortMidiStream stream,
			PmDeviceID inputDevice,
			IntPtr inputDriverInfo,
			long bufferSize,
			MidiTimeProcDelegate timeProc,
			IntPtr timeInfo);

		[DllImport ("portmidi")]
		public static extern PmError Pm_OpenOutput (
			out PortMidiStream stream,
			PmDeviceID outputDevice,
			IntPtr outputDriverInfo,
			long bufferSize,
			MidiTimeProcDelegate time_proc,
			IntPtr time_info,
			long latency);

		[DllImport ("portmidi")]
		public static extern PmError Pm_SetFilter (PortMidiStream stream, MidiFilter filters);

		// TODO
		public static long Pm_Channel (int channel) { return 1 << channel; }

		[DllImport ("portmidi")]
		public static extern PmError Pm_SetChannelMask (PortMidiStream stream, int mask);

		[DllImport ("portmidi")]
		public static extern PmError Pm_Abort (PortMidiStream stream);

		[DllImport ("portmidi")]
		public static extern PmError Pm_Close (PortMidiStream stream);

		// TODO
		public static long Pm_MessageStatus (long msg) { return ((msg) & 0xFF); }
		// TODO
		public static long Pm_MessageData1 (long msg) { return (((msg) >> 8) & 0xFF); }
		// TODO
		public static long Pm_MessageData2 (long msg) { return (((msg) >> 16) & 0xFF); }

		[DllImport ("portmidi")]
		public static extern int Pm_Read (PortMidiStream stream, MidiEvent [] buffer, long length);

		[DllImport ("portmidi")]
		public static extern PmError Pm_Poll (PortMidiStream stream);

		[DllImport ("portmidi")]
		public static extern PmError Pm_Write (PortMidiStream stream, IntPtr buffer, long length);

		[DllImport ("portmidi")]
		public static extern PmError Pm_WriteShort (PortMidiStream stream, PmTimestamp when, MidiMessage msg);

		[DllImport ("portmidi")]
		public static extern PmError Pm_WriteSysEx (PortMidiStream stream, PmTimestamp when, byte [] msg);
	}

	[StructLayout (LayoutKind.Sequential)]
	struct PmDeviceInfo
	{
		public int StructVersion;
		public IntPtr Interface; // char*
		public IntPtr Name; // char*
		public int Input; // 1 or 0
		public int Output; // 1 or 0
		public int Opened;
	}
}

