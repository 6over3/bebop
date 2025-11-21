from __future__ import annotations

from datetime import datetime, timezone
from struct import Struct
from typing import Any, TypeVar, Union
from uuid import UUID

# constants
_TICKS_BETWEEN_EPOCHS = 621355968000000000
_DATE_MASK = 0x3fffffffffffffff

# pre-compiled struct formats
_UINT16 = Struct("<H")
_INT16 = Struct("<h")
_UINT32 = Struct("<I")
_INT32 = Struct("<i")
_UINT64 = Struct("<Q")
_INT64 = Struct("<q")
_FLOAT32 = Struct("<f")
_FLOAT64 = Struct("<d")

class UnionDefinition:
    """
    A utility class for unions
    """
    __slots__ = ('discriminator', 'value')
    discriminator: int
    value: Any

    def __init__(self, discriminator: int, value: Any) -> None:
        self.discriminator = discriminator
        self.value = value

UnionType = TypeVar("UnionType", bound=UnionDefinition)

class BebopReader:
    """
    A wrapper around a bytearray for reading Bebop base types from it.

    It is used by the code that `bebopc --lang python` generates.
    You shouldn't need to use it directly.
    """
    __slots__ = ('_buffer', 'index')

    def __init__(self, buffer: Union[bytes, bytearray, memoryview, list] = b""):
        self._buffer = memoryview(bytes(buffer) if isinstance(buffer, list) else buffer)
        self.index = 0

    @classmethod
    def from_buffer(cls, buffer: Union[bytes, bytearray, memoryview, list]):
        return cls(buffer)

    def _skip(self, amount: int) -> None:
        self.index += amount

    def read_byte(self) -> int:
        byte = self._buffer[self.index]
        self.index += 1
        return byte

    def read_uint16(self) -> int:
        (v,) = _UINT16.unpack_from(self._buffer, self.index)
        self.index += 2
        return v

    def read_int16(self) -> int:
        (v,) = _INT16.unpack_from(self._buffer, self.index)
        self.index += 2
        return v

    def read_uint32(self) -> int:
        (v,) = _UINT32.unpack_from(self._buffer, self.index)
        self.index += 4
        return v

    def read_int32(self) -> int:
        (v,) = _INT32.unpack_from(self._buffer, self.index)
        self.index += 4
        return v

    def read_uint64(self) -> int:
        (v,) = _UINT64.unpack_from(self._buffer, self.index)
        self.index += 8
        return v

    def read_int64(self) -> int:
        (v,) = _INT64.unpack_from(self._buffer, self.index)
        self.index += 8
        return v

    def read_float32(self) -> float:
        (v,) = _FLOAT32.unpack_from(self._buffer, self.index)
        self.index += 4
        return v

    def read_float64(self) -> float:
        (v,) = _FLOAT64.unpack_from(self._buffer, self.index)
        self.index += 8
        return v

    def read_bool(self) -> bool:
        val = self._buffer[self.index]
        self.index += 1
        return val != 0

    def read_bytes(self) -> bytes:
        length = self.read_uint32()
        v = self._buffer[self.index : self.index + length]
        self.index += length
        return bytes(v)

    def read_string(self) -> str:
        length = self.read_uint32()
        string_data = self._buffer[self.index : self.index + length]
        self.index += length
        return str(string_data, 'utf-8')

    def read_guid(self) -> UUID:
        v = self._buffer[self.index : self.index + 16]
        self.index += 16
        return UUID(bytes_le=bytes(v))

    def read_date(self) -> datetime:
        ticks = self.read_uint64() & _DATE_MASK
        ms = (ticks - _TICKS_BETWEEN_EPOCHS) / 10000000
        return datetime.fromtimestamp(ms, tz=timezone.utc)

    read_message_length = read_uint32


class BebopWriter:
    """
    A wrapper around a bytearray for writing Bebop base types from it.

    It is used by the code that `bebopc --lang python` generates.
    You shouldn't need to use it directly.
    """
    __slots__ = ('_buffer',)

    def __init__(self):
        self._buffer = bytearray()

    @property
    def length(self) -> int:
        return len(self._buffer)

    def write_byte(self, val: int) -> None:
        self._buffer.append(val)

    def write_uint16(self, val: int) -> None:
        self._buffer += _UINT16.pack(val)

    def write_int16(self, val: int) -> None:
        self._buffer += _INT16.pack(val)

    def write_uint32(self, val: int) -> None:
        self._buffer += _UINT32.pack(val)

    def write_int32(self, val: int) -> None:
        self._buffer += _INT32.pack(val)

    def write_uint64(self, val: int) -> None:
        self._buffer += _UINT64.pack(val)

    def write_int64(self, val: int) -> None:
        self._buffer += _INT64.pack(val)

    def write_float32(self, val: float) -> None:
        self._buffer += _FLOAT32.pack(val)

    def write_float64(self, val: float) -> None:
        self._buffer += _FLOAT64.pack(val)

    def write_bool(self, val: bool) -> None:
        self._buffer.append(val)

    def write_bytes(self, val: Union[bytes, bytearray, memoryview], write_msg_length: bool = True) -> None:
        if write_msg_length:
            self._buffer += _UINT32.pack(len(val))
        self._buffer += val

    def write_string(self, val: str) -> None:
        self.write_bytes(val.encode("utf-8"))

    def write_guid(self, guid: UUID) -> None:
        self.write_bytes(guid.bytes_le, write_msg_length=False)

    def write_date(self, date: datetime) -> None:
        secs = date.timestamp()
        ticks = int(secs * 10000000) + _TICKS_BETWEEN_EPOCHS
        self.write_uint64(ticks & _DATE_MASK)

    def reserve_message_length(self) -> int:
        """
        Reserve some space to write a message's length prefix, and return its index.
        The length is stored as a little-endian fixed-width unsigned 32-bit integer, so 4 bytes are reserved.
        """
        i = len(self._buffer)
        self._buffer += b'\x00\x00\x00\x00'
        return i

    def fill_message_length(self, position: int, message_length: int) -> None:
        _UINT32.pack_into(self._buffer, position, message_length)

    def to_list(self) -> list[int]:
        return list(self._buffer)
