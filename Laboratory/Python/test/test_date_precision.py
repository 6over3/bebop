from datetime import datetime, timezone

from python_bebop.bebop import BebopReader, BebopWriter


def test_date_precision_and_utc():
    # Arrange: Create a writer and a date with specific microseconds in UTC
    writer = BebopWriter()
    expected_dt = datetime(2023, 1, 1, 12, 0, 0, 123456, tzinfo=timezone.utc)

    # Act: Write the date and read it back
    writer.write_date(expected_dt)
    buffer = writer.to_list()
    reader = BebopReader.from_buffer(bytearray(buffer))
    actual_dt = reader.read_date()

    # Assert: Verify precision (microseconds) and Timezone (UTC) are preserved
    assert actual_dt.microsecond == 123456
    assert actual_dt.tzinfo == timezone.utc
    assert actual_dt == expected_dt
