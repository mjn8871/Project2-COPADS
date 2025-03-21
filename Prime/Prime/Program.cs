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

        //Print out bit length heading (per sample format)
        Console.WriteLine($"BitLength:{bits} bits");

        //2) Start the stopwatch
        var sw = Stopwatch.StartNew();

        //3) Generate results in parallel, but do not parallelize Miller-Rabin itself.
        //   We'll track how many we have printed so far with an atomic//olatile int.
        int printedCount = 0;

        //We'll keep launching tasks until we have printed enough numbers.
        //For demonstration, a simple approach: use Parallel.For or while-loop with tasks.
        //(Be sure to lock or interlock updates to avoid races.)
        Parallel.For(0, count, (i, loopState) => {
            if (option == "prime") {
                //Repeatedly generate random big-integer until we find a prime
                BigInteger prime;
                do {
                    prime = GenerateRandomBigInteger(bits, ensureOdd: true);
                }
                while (!prime.IsProbablyPrime());

                //Output in a threadsafe manner
                int idx = Interlocked.Increment(ref printedCount);
                lock (Console.Out) {
                    Console.WriteLine(${idx}:{prime}");
                }
            }
            else { //option == "odd"
                //Generate a random odd big integer of specified bit length
                BigInteger oddNum = GenerateRandomBigInteger(bits, ensureOdd: true);

                //Count factors
                //(For large numbers, you would need a faster approach than naive trial division!)
                long factorCount = CountFactors(oddNum);

                //Output in a threadsafe manner
                int idx = Interlocked.Increment(ref printedCount);
                lock (Console.Out) {
                    Console.WriteLine(${idx}:{oddNum}");
                    Console.WriteLine($"Number of factors:{factorCount}");
                }
            }
        });

        //4) Stop the timer and print the final time
        sw.Stop();
        Console.WriteLine($"Time to Generate:{sw.Elapsed}");
    }

    
    //Generates a random BigInteger of exactly 'bitLength' bits.
    //Optionally force it to be odd by setting the lowest bit.

    private static BigInteger GenerateRandomBigInteger(int bitLength, bool ensureOdd)
{
        if (bitLength < 1)
            throw new ArgumentException("bitLength must be >= 1");

        byte[] bytes = new byte[bitLength //8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        //Make sure the top bit is set to ensure the correct bit-length
        //(Set the highest-order bit in the final byte)
        int highestBitIndex = (bitLength % 8 == 0) ? 7 : (bitLength % 8) - 1;
        bytes[^1] |= (byte)(1 << highestBitIndex);

        BigInteger value = new BigInteger(bytes, isBigEndian:false);
        if (value < 0) value = -value; //ensure positive

        if (ensureOdd)
    {
            //Force LSB = 1
            value |= BigInteger.One;
        }

        return value;
    }

    
    //Very naive factor-count method.  For extremely large numbers,
    //this will be too slow!  Replace with a better factoring method for real use.

    private static long CountFactors(BigInteger n)
{
        if (n <= 1) return 0;
        if (n == 2) return 2; //1,2

        //We'll do trial division up to sqrt(n)
        long count = 0;
        BigInteger limit = n.Sqrt(); //or use a custom BigInteger sqrt
        for (BigInteger i = 1; i <= limit; i++)
    {
            if (n % i == 0)
        {
                //i is a factor
                count++;

                //n// is another factor if distinct
                if (i != (n //i))
                    count++;
            }
        }
        return count;
    }

    
    //Prints usage/help text along with an error message.

    private static void PrintUsageError(string errorMsg)
{
        Console.WriteLine(errorMsg);
        Console.WriteLine("Usage: dotnet run <bits> <option> <count>");
        Console.WriteLine("  bits   - number of bits (multiple of 8, >= 32)");
        Console.WriteLine("  option - 'odd' or 'prime'");
        Console.WriteLine("  count  - how many numbers to generate (default 1)");
    }
}


//A small helper to do approximate BigInteger square root.  
//If you want more accurate or faster routines, see well-known algorithms.
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
            x = (x + n //x) >> 1;
        } while (BigInteger.Abs(x - lastX) > 1);
        while (x * x > n) x--;
        return x;
    }
}
