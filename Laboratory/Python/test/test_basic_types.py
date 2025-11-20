from schemas import BasicTypes
from datetime import datetime, timezone
from uuid import UUID


def test_write_read():
    bt = BasicTypes(
        a_bool=True,
        a_byte=1,
        a_int16=-2,
        a_uint16=2,
        a_int32=-4,
        a_uint32=4,
        a_int64=-8,
        a_uint64=8,
        a_float32=2.0,
        a_float64=2.0,
        a_string="a_string",
        a_guid=UUID("ff1ed055-1839-4d7f-84d3-5ca28fa298b9"),
        a_date=datetime(1999, 5, 17, tzinfo=timezone.utc),
    )

    encoded = BasicTypes.encode(bt)

    decoded = BasicTypes.decode(encoded)

    assert decoded.a_bool == True
    assert decoded.a_byte == 1
    assert decoded.a_int16 == -2
    assert decoded.a_uint16 == 2
    assert decoded.a_int32 == -4
    assert decoded.a_uint32 == 4
    assert decoded.a_int64 == -8
    assert decoded.a_uint64 == 8
    assert decoded.a_float32 == 2.0
    assert decoded.a_float64 == 2.0
    assert decoded.a_string == "a_string"
    assert decoded.a_guid == UUID("ff1ed055-1839-4d7f-84d3-5ca28fa298b9")
    assert decoded.a_date == datetime(1999, 5, 17, tzinfo=timezone.utc)


