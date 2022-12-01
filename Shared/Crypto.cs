﻿using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace Shared; 

public class Crypto {
    public static string Md5Hash(String input)
    {
        return Hash(MD5.Create(), new MemoryStream(Encoding.ASCII.GetBytes(input)));
    }

    public static uint Crc32Checksum(Array input) {
        byte[] output = new byte[Buffer.ByteLength(input)] ;
        Buffer.BlockCopy(input, 0, output, 0, output.Length);
        return BitConverter.ToUInt32(Crc32.Hash(output));
    }

    public static string Hash(HashAlgorithm alg, Stream stream)
    {
        return Convert.ToHexString(alg.ComputeHash(stream));
    }
}