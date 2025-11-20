from python_bebop.bebop import BebopReader, BebopWriter


def test_fill_message_length_writes_4_bytes():
    # Arrange: Prepare writer with reserved length slot and dummy payload
    writer = BebopWriter()
    length_index = writer.reserve_message_length()
    payload = bytearray(b"\x01" * 10)
    writer.write_bytes(payload, write_msg_length=False)

    # Act: Backfill the correct length into the reserved slot
    writer.fill_message_length(length_index, len(payload))

    # Assert: Verify the length was written correctly by reading it back
    reader = BebopReader.from_buffer(bytearray(writer.to_list()))

    read_length = reader.read_message_length()
    assert read_length == 10

    remaining = reader._buffer[reader.index :]
    assert remaining == payload
