import * as G from './generated/gen';
if (typeof require !== 'undefined') {
    if (typeof TextDecoder === 'undefined') (global as any).TextDecoder = require('util').TextDecoder;
}
it("Supports enum sizes", () => {
    expect(G.SmallEnum.B).toEqual(255);
    expect(typeof(G.SmallEnum.B)).toEqual("number");
    expect(G.HugeEnum.MaxInt.toString()).toEqual(0x7FFFFFFFFFFFFFFFn.toString());
    expect(typeof(G.HugeEnum.MaxInt)).toEqual("bigint");
    const buffer = G.SmallAndHuge.encode({ small: 123, huge: 123n });
    expect(buffer.length).toEqual(1 + 8);
    const decoded = G.SmallAndHuge.decode(buffer);
    expect(decoded.small).toEqual(123);
    expect(decoded.huge.toString()).toEqual("123");
    expect(G.HugeEnum[0x7FFFFFFFFFFFFFFFn.toString()]).toEqual("MaxInt");
});
