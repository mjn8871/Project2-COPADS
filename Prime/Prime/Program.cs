namespace Prime;

using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

//static class to hold BigInteger extension, including Miller-Rabin primality test.
public static class BigIntegerExtensions {
    
    //miller-Rabin primality test, returning true if 'value' is probably prime,
    //or false if definitely composite.
    public static bool IsProbablyPrime(this BigInteger value, int k = 10) {
        //small cases
        if (value < 2)  return false;
        if (value == 2) return true;
        if (value.IsEven) return false; //even => composite

        //write n-1 as 2^s * d with d odd
        BigInteger d = value - 1;
        int s = 0;
        while ((d & 1) == 0) { //equal to "while d is even" 
            d >>= 1;
            s++;
        }

        //witness loop
        for (int i = 0; i < k; i++) {
            //get random 'a' in [2..value-2]
            BigInteger a = RandomBigIntegerInRange(2, value - 2);

            //x = a^d mod value
            BigInteger x = BigInteger.ModPow(a, d, value);

            if (x == 1 || x == value - 1)
                continue; //might be prime

            bool foundComposite = false;
            for (int r = 1; r < s; r++) {
                x = BigInteger.ModPow(x, 2, value);
                if (x == 1)
                    return false; //composite
                if (x == value - 1) {
                    foundComposite = true;
                    break;
                }
            }
            if (!foundComposite && x != value - 1)
                return false; //composite
        }

        //probably prime
        return true;
    }

    
    //returns a random BigInteger in [minValue..maxValue], using RandomNumberGenerator.
    private static BigInteger RandomBigIntegerInRange(BigInteger minValue, BigInteger maxValue) {
        //assume minValue <= maxValue and both > 0
        BigInteger range = maxValue - minValue + 1;
        int byteCount = range.GetByteCount();

        //repeatedly generate random values until one is below range
        using var rng = RandomNumberGenerator.Create();
        while (true) {
            byte[] bytes = new byte[byteCount];
            rng.GetBytes(bytes);

            //convert to BigInteger
            BigInteger candidate = new BigInteger(bytes, isBigEndian:false);
            if (candidate < 0) candidate = -candidate;

            //if candidate < range, shift into [minValue..maxValue]
            if (candidate < range)
                return minValue + candidate;
        }
    }
}


//entry-point class for generating either large prime numbers or odd numbers & factor counts.
public class Program{
    public static void Main(string[] args) {
        //parse arguments:  <bits> <option> <count>
        if (args.Length < 2) {
            PrintUsageError("Not enough arguments.\n");
            return;
        }

        //attempt to parse bits
        if (!int.TryParse(args[0], out int bits)) {
            PrintUsageError($"Invalid 'bits' argument: {args[0]}' is not an integer.\n");
            return;
        }
        if (bits < 32 || (bits % 8) != 0) {
            PrintUsageError($"Invalid 'bits' argument: must be multiple of 8 and >= 32.\n");
            return;
        }

        //option must be odd or prime
        string option = args[1].Trim().ToLowerInvariant();
        if (option != "odd" && option != "prime") {
            PrintUsageError($"Invalid 'option': {option}'. Must be 'odd' or 'prime'.\n");
            return;
        }

        //count
        int count = 1;
        if (args.Length >= 3) {
            if (!int.TryParse(args[2], out count)) {
                PrintUsageError($"Invalid 'count' argument: {args[2]}' is not an integer.\n");
                return;
            }
            if (count < 1) {
                PrintUsageError($"Invalid 'count' argument: must be >= 1.\n");
                return;
            }
        }

        //print out bit length heading
        Console.WriteLine($"BitLength:{bits} bits");

        //start the stopwatch
        var sw = Stopwatch.StartNew();

        //generate results in parallel, but do not parallelize Miller-Rabin itself.
        int printedCount = 0;

        //keep launching tasks until we have printed enough numbers.
        Parallel.For(0, count, (i, loopState) => {
            if (option == "prime") {
                //repeatedly generate random big-integer until we find a prime
                BigInteger prime;
                do {
                    prime = GenerateRandomBigInteger(bits, ensureOdd: true);
                }
                while (!prime.IsProbablyPrime());

                //output in a threadsafe manner
                int idx = Interlocked.Increment(ref printedCount);
                lock (Console.Out) {
                    Console.WriteLine($"{idx}:{prime}");
                }
            }
            else { //option == "odd"
                //generate a random odd big integer of specified bit length
                BigInteger oddNum = GenerateRandomBigInteger(bits, ensureOdd: true);

                //count factors
                long factorCount = CountFactors(oddNum);

                //output in a threadsafe manner
                int idx = Interlocked.Increment(ref printedCount);
                lock (Console.Out) {
                    Console.WriteLine($"{idx}:{oddNum}");
                    Console.WriteLine($"Number of factors:{factorCount}");
                }
            }
        });

        //stop the timer and print the final time
        sw.Stop();
        Console.WriteLine($"Time to Generate:{sw.Elapsed}");
    }

    
    //generates a random BigInteger of exactly 'bitLength' bits.
    private static BigInteger GenerateRandomBigInteger(int bitLength, bool ensureOdd)
    {
        if (bitLength < 1)
            throw new ArgumentException("bitLength must be >= 1");

        byte[] bytes = new byte[bitLength / 8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        int highestBitIndex = (bitLength % 8 == 0) ? 7 : (bitLength % 8) - 1;
        bytes[^1] |= (byte)(1 << highestBitIndex);

        BigInteger value = new BigInteger(bytes, isBigEndian:false);
        if (value < 0) value = -value; //ensure positive

        if (ensureOdd)
        {
            //force LSB = 1
            value |= BigInteger.One;
        }

        return value;
    }
 
    //very naive factor-count method.  For extremely large numbers,
    //this will be too slow!  Replace with a better factoring method for real use.

    private static long CountFactors(BigInteger n)
    {
        if (n <= 1) return 0;
        if (n == 2) return 2; //1,2

        //we'll do trial division up to sqrt(n)
        long count = 0;
        BigInteger limit = n.Sqrt(); //or use a custom BigInteger sqrt
        for (BigInteger i = 1; i <= limit; i++)
        {
            if (n % i == 0)
            {
                //i is a factor
                count++;

                if (i != (n / i))
                    count++;
            }
        }
        return count;
    }

    
    //prints usage/help text along with an error message.

    private static void PrintUsageError(string errorMsg)
    {
        Console.WriteLine(errorMsg);
        Console.WriteLine("Usage: dotnet run <bits> <option> <count>");
        Console.WriteLine("  bits   - number of bits (multiple of 8, >= 32)");
        Console.WriteLine("  option - 'odd' or 'prime'");
        Console.WriteLine("  count  - how many numbers to generate (default 1)");
    }
}


//a small helper to do approximate BigInteger square root.
public static class BigIntegerSqrtExtension{
    public static BigInteger Sqrt(this BigInteger n)
    {
        if (n <= 0) return 0;
        //Newton's method
        BigInteger x = n >> 1; //divide by 2
        if (x == 0) return n;  //n < 2
        BigInteger lastX;
        do
        {
            lastX = x;
            // x = (x + n //x) >> 1;  Python style comment
            x = (x + n / x) >> 1;   // corrected for C# division
        } while (BigInteger.Abs(x - lastX) > 1);
        while (x * x > n) x--;
        return x;
    }
}
