// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;

namespace g3
{
internal sealed class BiharmonicCertificateRangeException : ArithmeticException { }
internal readonly struct BiharmonicFixedInterval
{
    public readonly BigInteger Lo, Hi;
    public const int Bits = 256;
    public static BigInteger Scale => BigInteger.One << Bits;
    public BiharmonicFixedInterval(BigInteger lo, BigInteger hi) {
        if (lo > hi) throw new ArgumentException();
        // Fractional precision is not an integer-size budget. Bound the signed
        // endpoint representations separately; transient products are at most
        // twice this bit length before they are rounded and checked again.
        if (lo.ToByteArray().Length > 512 || hi.ToByteArray().Length > 512) throw new BiharmonicCertificateRangeException();
        Lo=lo; Hi=hi;
    }
    static BigInteger Floor(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign < 0) { numerator=-numerator; denominator=-denominator; }
        BigInteger remainder; var quotient=BigInteger.DivRem(numerator,denominator,out remainder);
        return remainder.Sign < 0 ? quotient-1 : quotient;
    }
    static BigInteger Ceil(BigInteger numerator, BigInteger denominator) { return -Floor(-numerator,denominator); }
    public static BiharmonicFixedInterval FromDouble(double v)
    {
        if (!MathUtil.IsFinite(v)) throw new ArgumentException();
        long raw=BitConverter.DoubleToInt64Bits(v); bool negative=raw<0;
        int field=(int)((raw>>52)&0x7ff); long mantissa=raw&0x000fffffffffffffL;
        if(field!=0) mantissa|=1L<<52;
        int exponent=(field==0 ? -1074 : field-1075)+Bits;
        BigInteger numerator=negative ? -new BigInteger(mantissa) : new BigInteger(mantissa);
        if(exponent>=0) return new BiharmonicFixedInterval(numerator<<exponent,numerator<<exponent);
        BigInteger denominator=BigInteger.One << -exponent;
        return new BiharmonicFixedInterval(Floor(numerator,denominator),Ceil(numerator,denominator));
    }
    public static BiharmonicFixedInterval operator +(BiharmonicFixedInterval a, BiharmonicFixedInterval b) => new BiharmonicFixedInterval(a.Lo+b.Lo,a.Hi+b.Hi);
    public static BiharmonicFixedInterval operator -(BiharmonicFixedInterval a, BiharmonicFixedInterval b) => new BiharmonicFixedInterval(a.Lo-b.Hi,a.Hi-b.Lo);
    public static BiharmonicFixedInterval operator *(BiharmonicFixedInterval a, BiharmonicFixedInterval b)
    {
        var x=a.Lo*b.Lo;var y=a.Lo*b.Hi;var z=a.Hi*b.Lo;var w=a.Hi*b.Hi;
        var low=BigInteger.Min(BigInteger.Min(x,y),BigInteger.Min(z,w));
        var high=BigInteger.Max(BigInteger.Max(x,y),BigInteger.Max(z,w));
        return new BiharmonicFixedInterval(low>>Bits,-((-high)>>Bits));
    }
    public static BiharmonicFixedInterval operator /(BiharmonicFixedInterval a, BiharmonicFixedInterval b)
    {
        if(b.Lo.Sign<=0 && b.Hi.Sign>=0) throw new DivideByZeroException();
        var lo=new[]{Floor(a.Lo<<Bits,b.Lo),Floor(a.Lo<<Bits,b.Hi),Floor(a.Hi<<Bits,b.Lo),Floor(a.Hi<<Bits,b.Hi)};
        var hi=new[]{Ceil(a.Lo<<Bits,b.Lo),Ceil(a.Lo<<Bits,b.Hi),Ceil(a.Hi<<Bits,b.Lo),Ceil(a.Hi<<Bits,b.Hi)};
        var l=lo[0];var h=hi[0];for(int i=1;i<4;i++){l=BigInteger.Min(l,lo[i]);h=BigInteger.Max(h,hi[i]);}
        return new BiharmonicFixedInterval(l,h);
    }
    public static BigInteger IntegerSqrt(BigInteger n)
    {
        if(n.Sign<0) throw new ArgumentException(); if(n.IsZero) return BigInteger.Zero;
        BigInteger x=BigInteger.One << ((n.ToByteArray().Length * 8 + 1)/2);
        while(true) { var next=(x+n/x)>>1; if(next>=x) return x; x=next; }
    }
    public static BiharmonicFixedInterval Sqrt(BiharmonicFixedInterval a)
    {
        if(a.Hi.Sign<0) throw new ArgumentException();
        BigInteger low=BigInteger.Max(BigInteger.Zero,a.Lo)<<Bits, high=a.Hi<<Bits;
        BigInteger l=IntegerSqrt(low),h=IntegerSqrt(high);
        if(h*h<high)h++;
        return new BiharmonicFixedInterval(l,h);
    }
}

}
