using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Commons.Music.Midi
{
	public class SmfMusic
	{
		List<SmfTrack> tracks = new List<SmfTrack> ();

		public SmfMusic ()
		{
			Format = 1;
		}

		public short DeltaTimeSpec { get; set; }

		public byte Format { get; set; }

		public void AddTrack (SmfTrack track)
		{
			this.tracks.Add (track);
		}

		public IList<SmfTrack> Tracks {
			get { return tracks; }
		}
	}

	public class SmfTrack
	{
		List<SmfEvent> events = new List<SmfEvent> ();

		public void AddEvent (SmfEvent evt)
		{
			events.Add (evt);
		}

		public IList<SmfEvent> Events {
			get { return events; }
		}
	}

	public struct SmfEvent
	{
		public SmfEvent (int deltaTime, SmfMessage msg)
		{
			DeltaTime = deltaTime;
			Message = msg;
		}

		public readonly int DeltaTime;
		public readonly SmfMessage Message;
	}

	public struct SmfMessage
	{
		public const byte NoteOff = 0x80;
		public const byte NoteOn = 0x90;
		public const byte PAf = 0xA0;
		public const byte CC = 0xB0;
		public const byte Program = 0xC0;
		public const byte CAf = 0xD0;
		public const byte Pitch = 0xE0;
		public const byte SysEx1 = 0xF0;
		public const byte SysEx2 = 0xF7;
		public const byte Meta = 0xFF;

		public const byte EndSysEx = 0xF7;

		public SmfMessage (int value)
		{
			Value = value;
			Data = null;
		}

		public SmfMessage (byte type, byte arg1, byte arg2, byte [] data)
		{
			Value = type + (arg1 << 8) + (arg2 << 16);
			Data = data;
		}

		public readonly int Value;

		// This expects EndSysEx byte _inclusive_ for F0 message.
		public readonly byte [] Data;

		public byte StatusByte {
			get { return (byte) (Value & 0xFF); }
		}

		public byte MessageType {
			get {
				switch (StatusByte) {
				case Meta:
				case SysEx1:
				case SysEx2:
					return StatusByte;
				default:
					return (byte) (Value & 0xF0);
				}
			}
		}

		public byte Msb {
			get { return (byte) ((Value & 0xFF00) >> 8); }
		}

		public byte Lsb {
			get { return (byte) ((Value & 0xFF0000) >> 16); }
		}

		public byte MetaType {
			get { return Msb; }
		}

		public byte Channel {
			get { return (byte) (Value & 0x0F); }
		}

		public static byte FixedDataSize (byte statusByte)
		{
			switch (statusByte & 0xF0) {
			case 0xF0: // and 0xF7, 0xFF
				return 0; // no fixed data
			case Program: // ProgramChg
			case CAf: // CAf
				return 1;
			default:
				return 2;
			}
		}
	}

	public class SmfWriter
	{
		Stream stream;

		public SmfWriter (Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");
			this.stream = stream;
		}

		void WriteShort (short v)
		{
			stream.WriteByte ((byte) (v / 0x100));
			stream.WriteByte ((byte) (v % 0x100));
		}

		void WriteInt (int v)
		{
			stream.WriteByte ((byte) (v / 0x1000000));
			stream.WriteByte ((byte) (v / 0x10000 & 0xFF));
			stream.WriteByte ((byte) (v / 0x100 & 0xFF));
			stream.WriteByte ((byte) (v % 0x100));
		}

		public void WriteHeader (short format, short tracks, short deltaTimeSpec)
		{
			stream.Write (Encoding.ASCII.GetBytes ("MThd"), 0, 4);
			WriteShort (0);
			WriteShort (6);
			WriteShort (format);
			WriteShort (tracks);
			WriteShort (deltaTimeSpec);
		}

		public void WriteTrack (SmfTrack track)
		{
			stream.Write (Encoding.ASCII.GetBytes ("MTrk"), 0, 4);
			WriteInt (GetTrackDataSize (track));

			byte running_status = 0;

			foreach (SmfEvent e in track.Events) {
				Write7BitVariableInteger (e.DeltaTime);
				if (running_status >= 0xF0 || e.Message.StatusByte != running_status)
					stream.WriteByte (e.Message.StatusByte);
				running_status = e.Message.StatusByte;
				switch (e.Message.MessageType) {
				case SmfMessage.Meta:
					stream.WriteByte (e.Message.MetaType);
					Write7BitVariableInteger (e.Message.Data.Length);
					stream.Write (e.Message.Data, 0, e.Message.Data.Length);
					break;
				case SmfMessage.SysEx1:
				case SmfMessage.SysEx2:
					Write7BitVariableInteger (e.Message.Data.Length);
					stream.Write (e.Message.Data, 0, e.Message.Data.Length);
					break;
				default:
					int len = SmfMessage.FixedDataSize (e.Message.MessageType);
					stream.WriteByte (e.Message.Msb);
					if (len > 1)
						stream.WriteByte (e.Message.Lsb);
					if (len > 2)
						throw new Exception ("Unexpected data size: " + len);
					break;
				}
			}
		}

		int GetVariantLength (int value)
		{
			if (value == 0)
				return 1;
			int ret = 0;
			for (int x = value; x != 0; x >>= 7)
				ret++;
			return ret;
		}

		int GetTrackDataSize (SmfTrack track)
		{
			int size = 0;
			byte running_status = 0;
			foreach (SmfEvent e in track.Events) {
				// delta time
				size += GetVariantLength (e.DeltaTime);

				// message type & channel
				if (running_status >= 0xF0 || running_status != e.Message.StatusByte)
					size++;
				running_status = e.Message.StatusByte;

				// arguments
				switch (e.Message.MessageType) {
				case SmfMessage.Meta:
					size++; // MetaType
					goto case SmfMessage.SysEx1;
				case SmfMessage.SysEx1:
				case SmfMessage.SysEx2:
					size += GetVariantLength (e.Message.Data.Length);
					size += e.Message.Data.Length;
					break;
				default:
					size += SmfMessage.FixedDataSize (e.Message.MessageType);
					break;
				}
			}
			return size;
		}

		void Write7BitVariableInteger (int value)
		{
			Write7BitVariableInteger (value, false);
		}

		void Write7BitVariableInteger (int value, bool shifted)
		{
			if (value == 0) {
				stream.WriteByte ((byte) (shifted ? 0x80 : 0));
				return;
			}
			if (value > 0x80)
				Write7BitVariableInteger (value >> 7, true);
			stream.WriteByte ((byte) ((value & 0x7F) + (shifted ? 0x80 : 0)));
		}
	}

	public class SmfReader
	{
		public SmfReader (Stream stream)
		{
			this.stream = stream;
		}

		Stream stream;
		SmfMusic data = new SmfMusic ();

		public SmfMusic Music { get { return data; } }

		public void Parse ()
		{
			if (
			    ReadByte ()  != 'M'
			    || ReadByte ()  != 'T'
			    || ReadByte ()  != 'h'
			    || ReadByte ()  != 'd')
				throw ParseError ("MThd is expected");
			if (ReadInt32 () != 6)
				throw ParseError ("Unexpeted data size (should be 6)");
			data.Format = (byte) ReadInt16 ();
			int tracks = ReadInt16 ();
			data.DeltaTimeSpec = ReadInt16 ();
			try {
				for (int i = 0; i < tracks; i++)
					data.Tracks.Add (ReadTrack ());
			} catch (FormatException ex) {
				throw ParseError ("Unexpected data error", ex);
			}
		}

		SmfTrack ReadTrack ()
		{
			var tr = new SmfTrack ();
			if (
			    ReadByte ()  != 'M'
			    || ReadByte ()  != 'T'
			    || ReadByte ()  != 'r'
			    || ReadByte ()  != 'k')
				throw ParseError ("MTrk is expected");
			int trackSize = ReadInt32 ();
			current_track_size = 0;
			int total = 0;
			while (current_track_size < trackSize) {
				int delta = ReadVariableLength ();
				tr.Events.Add (ReadEvent (delta));
				total += delta;
			}
			if (current_track_size != trackSize)
				throw ParseError ("Size information mismatch");
			return tr;
		}

		int current_track_size;
		byte running_status;

		SmfEvent ReadEvent (int deltaTime)
		{
			byte b = PeekByte ();
			running_status = b < 0x80 ? running_status : ReadByte ();
			int len;
			switch (running_status) {
			case SmfMessage.SysEx1:
			case SmfMessage.SysEx2:
			case SmfMessage.Meta:
				byte metaType = running_status == SmfMessage.Meta ? ReadByte () : (byte) 0;
				len = ReadVariableLength ();
				byte [] args = new byte [len];
				if (len > 0)
					ReadBytes (args);
				return new SmfEvent (deltaTime, new SmfMessage (running_status, metaType, 0, args));
			default:
				int value = running_status;
				value += ReadByte () << 8;
				if (SmfMessage.FixedDataSize (running_status) == 2)
					value += ReadByte () << 16;
				return new SmfEvent (deltaTime, new SmfMessage (value));
			}
		}

		void ReadBytes (byte [] args)
		{
			current_track_size += args.Length;
			int start = 0;
			if (peek_byte >= 0) {
				args [0] = (byte) peek_byte;
				peek_byte = -1;
				start = 1;
			}
			int len = stream.Read (args, start, args.Length - start);
			try {
			if (len < args.Length - start)
				throw ParseError (String.Format ("The stream is insufficient to read {0} bytes specified in the SMF event. Only {1} bytes read.", args.Length, len));
			} finally {
				stream_position += len;
			}
		}

		int ReadVariableLength ()
		{
			int val = 0;
			for (int i = 0; i < 4; i++) {
				byte b = ReadByte ();
				val = (val << 7) + b;
				if (b < 0x80)
					return val;
				val -= 0x80;
			}
			throw ParseError ("Delta time specification exceeds the 4-byte limitation.");
		}

		int peek_byte = -1;
		int stream_position;

		byte PeekByte ()
		{
			if (peek_byte < 0)
				peek_byte = stream.ReadByte ();
			if (peek_byte < 0)
				throw ParseError ("Insufficient stream. Failed to read a byte.");
			return (byte) peek_byte;
		}

		byte ReadByte ()
		{
			try {

			current_track_size++;
			if (peek_byte >= 0) {
				byte b = (byte) peek_byte;
				peek_byte = -1;
				return b;
			}
			int ret = stream.ReadByte ();
			if (ret < 0)
				throw ParseError ("Insufficient stream. Failed to read a byte.");
			return (byte) ret;

			} finally {
				stream_position++;
			}
		}

		short ReadInt16 ()
		{
			return (short) ((ReadByte () << 8) + ReadByte ());
		}

		int ReadInt32 ()
		{
			return (((ReadByte () << 8) + ReadByte () << 8) + ReadByte () << 8) + ReadByte ();
		}

		Exception ParseError (string msg)
		{
			return ParseError (msg, null);
		}

		Exception ParseError (string msg, Exception innerException)
		{
			throw new SmfParserException (String.Format (msg + "(at {0})", stream_position), innerException);
		}
	}

	public class SmfParserException : Exception
	{
		public SmfParserException () : this ("SMF parser error") {}
		public SmfParserException (string message) : base (message) {}
		public SmfParserException (string message, Exception innerException) : base (message, innerException) {}
	}
}
