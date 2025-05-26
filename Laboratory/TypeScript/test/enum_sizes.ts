import * as G from './generated/gen';
if (typeof require !== 'undefined') {
    if (typeof TextDecoder === 'undefined') (global as any).TextDecoder = require('util').TextDecoder;
}
it("Supports enum sizes", () => {
    expect(G.SmallEnum.B).toEqual(255);
    expect(typeof(G.SmallEnum.B)).toEqual("number");
    expect(G.HugeEnum.MaxInt.toString()).toEqual(0x7FFFFFFFFFFFFFFFn.toString());
    expect(typeof(G.HugeEnum.MaxInt)).toEqual("bigint");
    const buffer = G.SmallAndHuge.encode({ small: 255, huge: 123n });
    expect(buffer.length).toEqual(1 + 8);
    const decoded = G.SmallAndHuge.decode(buffer);
    expect(decoded.small).toEqual(255);
    expect(decoded.huge.toString()).toEqual("123");
    console.log(G.HugeEnum);
    expect(G.HugeEnum.MaxInt).toEqual(9223372036854775807n);
});
