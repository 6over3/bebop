use bebop::{collection, Record};
use criterion::{black_box, criterion_group, Criterion};
use protobuf::Message;

use bebops::jazz as bb;
use benchmarking::*;
use native::jazz as na;
use protos::jazz as pr;

const ALLOC_SIZE: usize = 1024 * 1024 * 4;

fn library(c: &mut Criterion) {
    // structuring
    // let mut structuring_group = c.benchmark_group("Jazz Library Structuring");
    // structuring_group.bench_function("Native", |b| {
    //     b.iter(|| {
    //         make_library();
    //     })
    // });
    //
    // don't penalize the others for the initial allocation
    let native_struct = na::make_library();
    // structuring_group.bench_function("Bebop", |b| {
    //     b.iter(|| {
    //         bb::Library::from(black_box(&native_struct));
    //     })
    // });
    // structuring_group.bench_function("Protobuf", |b| {
    //     b.iter(|| {
    //         // well protobuf requires cloning, so we might need a test with the cloning extracted, but
    //         // I also suspect it is quite common in real world to need to clone the data.
    //         // TODO: Figure out how to test without cloning in the performance test?
    //         pr::Library::from(black_box(native_struct.clone()));
    //     })
    // });
    // structuring_group.finish();

    // serialization
    let bebop_struct = bb::Library::from(&native_struct);
    let proto_struct = pr::Library::from(native_struct.clone());
    let mut buf = Vec::with_capacity(ALLOC_SIZE);
    let mut serialization_group = c.benchmark_group("Jazz Library Serialization");

    // serialization_group.bench_function("Json", |b| {
    //     b.iter(|| {
    //         serde_json::to_writer(&mut buf, &native_struct).unwrap();
    //         assert!(buf.len() <= ALLOC_SIZE);
    //         buf.clear();
    //     })
    // });

    // serialization_group.bench_function("Bincode", |b| {
    //     b.iter(|| {
    //         bincode::serialize_into(&mut buf, &native_struct).unwrap();
    //         assert!(buf.len() <= ALLOC_SIZE);
    //         buf.clear();
    //     })
    // });

    serialization_group.bench_function("MessagePack", |b| {
        b.iter(|| {
            rmp_serde::encode::write(&mut buf, &native_struct).unwrap();
            assert!(buf.len() <= ALLOC_SIZE);
            buf.clear();
        })
    });

    serialization_group.bench_function("Bebop", |b| {
        b.iter(|| {
            bebop_struct.serialize(&mut buf).unwrap();
            assert!(buf.len() <= ALLOC_SIZE);
            buf.clear();
        })
    });

    serialization_group.bench_function("Protobuf", |b| {
        b.iter(|| {
            proto_struct.write_to_writer(&mut buf).unwrap();
            assert!(buf.len() <= ALLOC_SIZE);
            buf.clear();
        })
    });

    serialization_group.finish();

    let mut deserialization_group = c.benchmark_group("Jazz Library Deserialization");
    // serde_json::to_writer(&mut buf, &native_struct).unwrap();
    // deserialization_group.bench_function("Json", |b| {
    //     b.iter(|| {
    //         serde_json::from_reader::<_, na::Library>(black_box(&*buf)).unwrap();
    //     })
    // });
    // buf.clear();

    // bincode::serialize_into(&mut buf, &native_struct).unwrap();
    // deserialization_group.bench_function("Bincode", |b| {
    //     b.iter(|| {
    //         bincode::deserialize_from::<_, na::Library>(black_box(&*buf)).unwrap();
    //     })
    // });
    // buf.clear();

    rmp_serde::encode::write(&mut buf, &native_struct).unwrap();
    deserialization_group.bench_function("MessagePack", |b| {
        b.iter(|| {
            rmp_serde::from_read::<_, na::Library>(black_box(&*buf)).unwrap();
        })
    });
    buf.clear();

    bebop_struct.serialize(&mut buf).unwrap();
    deserialization_group.bench_function("Bebop", |b| {
        b.iter(|| {
            bb::Library::deserialize(black_box(&buf)).unwrap();
        })
    });
    buf.clear();

    proto_struct.write_to_writer(&mut buf).unwrap();
    deserialization_group.bench_function("Protobuf", |b| {
        b.iter(|| {
            pr::Library::parse_from_bytes(&buf).unwrap();
        })
    });
}

criterion_group!(benches, library);
