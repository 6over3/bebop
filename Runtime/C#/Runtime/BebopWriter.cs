﻿using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Bebop.Exceptions;
using static System.Runtime.CompilerServices.Unsafe;
using static System.Runtime.InteropServices.MemoryMarshal;

namespace Bebop.Runtime
{
    /// <summary>
    /// A Bebop writer runtime implementation for .NET
    /// </summary>
    public ref struct BebopWriter
    {
        const long TicksBetweenEpochs = 621355968000000000L;
        const long DateMask = 0x3fffffffffffffffL;

        // ReSharper disable once InconsistentNaming
        private static readonly UTF8Encoding UTF8 = new();

        /// <summary>
        /// Track if the <see cref="_buffer"/> variable can be grown to a new instance
        /// </summary>
        private bool _allowBufferResizing;

        /// <summary>
        ///     A contiguous region of memory that contains the contents of a Bebop message
        /// </summary>
        private Span<byte> _buffer;

        /// <summary>
        ///     The amount of bytes that have been written to the underlying buffer.
        ///     <remarks>
        ///         This is not the same as the <see cref="_buffer"/> length which contains null-bytes due to look-ahead
        ///         allocation.
        ///     </remarks>
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// Converts an array into a <see cref="ImmutableArray{T}"/> without copying
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array"></param>
        /// <returns></returns>
        [MethodImpl(BebopConstants.HotPath)]
        private static ImmutableArray<T> AsImmutable<T>(T[] array) => As<T[], ImmutableArray<T>>(ref array);

        /// <summary>
        ///     Allocates a new <see cref="BebopWriter"/> instance backed by an empty array.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter Create() => new(Array.Empty<byte>(), allowBufferResizing: true);

        /// <summary>
        ///     Allocates a new <see cref="BebopWriter"/> instance backed by a new array of the given size.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter Create(int initialCapacity) => new(new byte[initialCapacity], allowBufferResizing: true);

        /// <summary>
        ///     Allocates a new <see cref="BebopWriter"/> instance backed by the given array instance.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter Create(byte[] buffer) => new(buffer, allowBufferResizing: false);

        /// <summary>
        /// Creates a new <see cref="BebopWriter"/> instance from the specified <paramref name="buffer"/>
        /// </summary>
        /// <param name="buffer">The buffer a Bebop record will be written to.</param>
        /// <returns>The initialized <see cref="BebopWriter"/></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter From(Memory<byte> buffer) => From(buffer.Span);
        /// <summary>
        /// Creates a new <see cref="BebopWriter"/> instance from the specified <paramref name="buffer"/>
        /// </summary>
        /// <param name="buffer">The buffer a Bebop record will be written to.</param>
        /// <returns>The initialized <see cref="BebopWriter"/></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter From(byte[] buffer) => new(buffer, allowBufferResizing: true);
        /// <summary>
        /// Creates a new <see cref="BebopWriter"/> instance from the specified <paramref name="buffer"/>
        /// </summary>
        /// <param name="buffer">The buffer a Bebop record will be written to.</param>
        /// <returns>The initialized <see cref="BebopWriter"/></returns>
        [MethodImpl(BebopConstants.HotPath)]
        public static BebopWriter From(Span<byte> buffer) => new(buffer, allowBufferResizing: true);

        /// <summary>
        ///     Creates a read-only slice of the underlying <see cref="_buffer"/> containing all currently written data.
        /// </summary>
        [MethodImpl(BebopConstants.HotPath)]
        public ReadOnlySpan<byte> Slice() => _buffer.Slice(0, Length);

        /// <summary>
        ///     Copies the contents of <see cref="Slice"/> into a new heap-allocated array.
        /// </summary>
        [MethodImpl(BebopConstants.HotPath)]
        public byte[] ToArray() => Slice().ToArray();

        /// <summary>
        ///     Copies the contents of <see cref="Slice"/> into a new immutable heap-allocated array.
        /// </summary>
        [MethodImpl(BebopConstants.HotPath)]
        public ImmutableArray<byte> ToImmutableArray() => AsImmutable(Slice().ToArray());

        /// <summary>
        /// Creates a new writer from the specified <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="allowBufferResizing"></param>
        private BebopWriter(Span<byte> buffer, bool allowBufferResizing)
        {
            if (!BitConverter.IsLittleEndian)
            {
                throw new BebopViewException("Big-endian systems are not supported by Bebop.");
            }
            _buffer = buffer;
            _allowBufferResizing = allowBufferResizing;
            Length = 0;
        }

        /// <summary>
        /// Allocates more space to the current writing process.
        /// </summary>
        /// <param name="amount"></param>
        [MethodImpl(BebopConstants.HotPath)]
        private void GrowBy(int amount)
        {
            if ((Length & 0xC0000000) != 0)
            {
                throw new BebopViewException("A Bebop View cannot grow beyond 2 gigabytes.");
            }
            if (Length + amount > _buffer.Length)
            {
                if (_allowBufferResizing)
                {
                    var newBuffer = new Span<byte>(new byte[(Length + amount) << 1]);
                    _buffer.CopyTo(newBuffer);
                    _buffer = newBuffer;
                }
                else
                {
                    throw new BebopViewException("This Bebop View cannot grow the buffer.");
                }
            }
            Length += amount;
        }

        /// <summary>
        ///     Writes a one-byte Boolean value to the current buffer, with 0 representing false and 1 representing true.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteByte(bool value)
        {
            const int size = 1;
            var index = Length;
            GrowBy(size);
            _buffer[index] = (byte)(value is false ? 0 : 1);
        }

        /// <summary>
        ///     Writes an unsigned byte to the current buffer and advances the position by one byte.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteByte(byte value)
        {
            const int size = 1;

            var index = Length;
            GrowBy(size);
            _buffer[index] = value;
        }

        /// <summary>
        ///     Writes a two-byte unsigned integer to the current buffer and advances the position by two bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteUInt16(ushort value)
        {
            const int size = 2;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }

        /// <summary>
        ///     Writes a two-byte signed integer to the current buffer and advances the position by two bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteInt16(short value)
        {
            const int size = 2;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }

        /// <summary>
        ///     Writes a four-byte unsigned integer to the current buffer and advances the position by four bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteUInt32(uint value)
        {
            const int size = 4;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }

        /// <summary>
        ///     Writes a four-byte signed integer to the current buffer and advances the position by four bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteInt32(int value)
        {
            const int size = 4;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }


        /// <summary>
        ///     Writes an eight-byte unsigned integer to the current buffer and advances the position by eight bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteUInt64(ulong value)
        {
            const int size = 8;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }

        /// <summary>
        ///     Writes an eight-byte signed integer to the current buffer and advances the position by eight bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteInt64(long value)
        {
            const int size = 8;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }

        /// <summary>
        /// Writes an four-byte floating point value to the current buffer and advances the position by four bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteFloat32(float value)
        {
            const int size = 4;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }


        /// <summary>
        ///     Writes an eight-byte floating-point value to the current buffer and advances the position by eight bytes.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteFloat64(double value)
        {
            const int size = 8;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), value);
        }
        /// <summary>
        /// Writes a <see cref="DateTime"/> (converted to UTC) to the current buffer and advances the position by eight bytes.
        /// </summary>
        /// <param name="date"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteDate(DateTime date)
        {
            long ms = (long)(date.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            long ticks = ms * 10000L + TicksBetweenEpochs;
            WriteInt64(ticks & DateMask);
        }

        /// <summary>
        /// Writes a <see cref="Guid"/> to the underlying buffer.
        /// </summary>
        /// <param name="guid"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteGuid(Guid guid)
        {
            const int size = 16;
            var index = Length;
            GrowBy(size);
            WriteUnaligned(ref GetReference(_buffer.Slice(index, size)), guid);
        }

        /// <summary>
        ///     Writes a length-prefixed UTF-8 string to this buffer, and advances the current position of the buffer in accordance
        ///     with the the specific characters being written to the buffer.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteString(string value)
        {
            if (value.Length == 0)
            {
                WriteUInt32(0);
                return;
            }
            unsafe
            {
                fixed (char* c = value)
                {
                    var size = UTF8.GetByteCount(c, value.Length);
                    WriteUInt32(unchecked((uint)size));
                    var index = Length;
                    GrowBy(size);
                    fixed (byte* o = _buffer.Slice(index, size))
                    {
                        UTF8.GetBytes(c, value.Length, o, size);
                    }
                }
            }
        }

        /// <summary>
        ///     Reserve some space to write a record's length prefix, and return its index.
        ///     The length is stored as a little-endian fixed-width unsigned 32 - bit integer, so 4 bytes are reserved.
        /// </summary>
        /// <returns>The index to later write the record's length to.</returns>
        [MethodImpl(BebopConstants.HotPath)]
        public int ReserveRecordLength()
        {
            const int size = 4;
            var i = Length;
            GrowBy(size);
            return i;
        }

        /// <summary>
        ///     Fill in a record's length prefix.
        /// </summary>
        /// <param name="position">The position in the buffer of the message's length prefix.</param>
        /// <param name="messageLength">The message length to write.</param>
        [MethodImpl(BebopConstants.HotPath)]
        public void FillRecordLength(int position, uint messageLength)
        {
            const int size = 4;
            WriteUnaligned(ref GetReference(_buffer.Slice(position, size)), messageLength);
        }

        /// <summary>
        /// Writes an array of <see cref="float"/> values to the underlying buffer.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteFloat32S(float[] value)
        {
            WriteUInt32(unchecked((uint)value.Length));
            var index = Length;
            var floatBytes = AsBytes<float>(value);
            if (floatBytes.IsEmpty)
            {
                return;
            }
            GrowBy(floatBytes.Length);
            floatBytes.CopyTo(_buffer.Slice(index, floatBytes.Length));
        }
        /// <summary>
        /// Writes an array of <see cref="double"/> values to the underlying buffer.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteFloat64S(double[] value)
        {
            WriteUInt32(unchecked((uint)value.Length));
            var index = Length;
            var doubleBytes = AsBytes<double>(value);
            if (doubleBytes.IsEmpty)
            {
                return;
            }
            GrowBy(doubleBytes.Length);
            doubleBytes.CopyTo(_buffer.Slice(index, doubleBytes.Length));
        }

        /// <summary>
        ///     Writes a byte array to the underlying buffer.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteBytes(ImmutableArray<byte> value)
        {
            WriteUInt32(unchecked((uint)value.Length));
            if (value.Length == 0)
            {
                return;
            }
            var index = Length;
            GrowBy(value.Length);
            value.AsSpan().CopyTo(_buffer.Slice(index, value.Length));
        }

        /// <summary>
        ///     Writes a byte array to the underlying buffer.
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(BebopConstants.HotPath)]
        public void WriteBytes(byte[] value)
        {
            WriteUInt32(unchecked((uint)value.Length));
            if (value.Length == 0)
            {
                return;
            }
            var index = Length;
            GrowBy(value.Length);
            value.AsSpan().CopyTo(_buffer.Slice(index, value.Length));
        }
    }
}
