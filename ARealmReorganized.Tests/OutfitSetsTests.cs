using ARealmReorganized.Logic;

namespace ARealmReorganized.Tests;

public class OutfitSetsTests
{
    [Fact]
    public void NoBitsSet_EveryPieceIsStored()
    {
        for (var slot = 0; slot < OutfitSets.SlotCount; slot++)
            Assert.True(OutfitSets.IsPieceStored(0, 0, slot));
    }

    [Fact]
    public void SetBit_MeansThatSlotIsEmpty()
    {
        const byte firstAndThirdEmpty = 0b0000_0101;

        Assert.False(OutfitSets.IsPieceStored(firstAndThirdEmpty, 0, 0));
        Assert.True(OutfitSets.IsPieceStored(firstAndThirdEmpty, 0, 1));
        Assert.False(OutfitSets.IsPieceStored(firstAndThirdEmpty, 0, 2));
        Assert.True(OutfitSets.IsPieceStored(firstAndThirdEmpty, 0, 3));
    }

    [Fact]
    public void SlotsPastTheFirstByte_ComeFromTheSecond()
    {
        const byte ringEmpty = 0b0000_0100; // slot 10 is bit 2 of the second byte

        Assert.True(OutfitSets.IsPieceStored(0, ringEmpty, 8));
        Assert.True(OutfitSets.IsPieceStored(0, ringEmpty, 9));
        Assert.False(OutfitSets.IsPieceStored(0, ringEmpty, 10));
        Assert.True(OutfitSets.IsPieceStored(0xFF, ringEmpty, 8));
    }
}
