﻿using System;
using System.Collections.Generic;
using System.IO;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security.Certificates;
using Org.BouncyCastle.Utilities.IO;

namespace Org.BouncyCastle.X509
{
    /**
     * class for dealing with X509 certificates.
     * <p>
     * At the moment this will deal with "-----BEGIN CERTIFICATE-----" to "-----END CERTIFICATE-----"
     * base 64 encoded certs, as well as the BER binaries of certificates and some classes of PKCS#7
     * objects.</p>
     */
    public class X509CertificateParser
    {
        private static readonly PemParser PemCertParser = new PemParser("CERTIFICATE");

        private Asn1Set sData;
        private int sDataObjectCount;
        private Stream currentStream;

        private X509Certificate ReadDerCertificate(Asn1InputStream dIn)
        {
            Asn1Sequence seq = (Asn1Sequence)dIn.ReadObject();

            if (seq.Count > 1 && seq[0] is DerObjectIdentifier contentType)
            {
                if (PkcsObjectIdentifiers.SignedData.Equals(contentType))
                {
                    if (Asn1Utilities.TryGetOptionalContextTagged(seq[1], 0, true, out var signedData,
                        SignedData.GetTagged))
                    {
                        sData = signedData.Certificates;
                        return GetCertificate();
                    }
                }
            }

            return new X509Certificate(X509CertificateStructure.GetInstance(seq));
        }

        private X509Certificate ReadPemCertificate(Stream inStream)
        {
            Asn1Sequence seq = PemCertParser.ReadPemObject(inStream);

            return seq == null ? null : new X509Certificate(X509CertificateStructure.GetInstance(seq));
        }

        private X509Certificate GetCertificate()
        {
            if (sData != null)
            {
                while (sDataObjectCount < sData.Count)
                {
                    var certificate = X509CertificateStructure.GetOptional(sData[sDataObjectCount++]);
                    if (certificate != null)
                        return new X509Certificate(certificate);
                }
            }

            return null;
        }

        /// <summary>
        /// Create loading data from byte array.
        /// </summary>
        /// <param name="input"></param>
        public X509Certificate ReadCertificate(byte[] input)
        {
            using (var inStream = new MemoryStream(input, false))
            {
                return ReadCertificate(inStream);
            }
        }

        /// <summary>
        /// Create loading data from byte array.
        /// </summary>
        /// <param name="input"></param>
        public IList<X509Certificate> ReadCertificates(byte[] input)
        {
            using (var inStream = new MemoryStream(input, false))
            {
                return ReadCertificates(inStream);
            }
        }

        /**
         * Generates a certificate object and initializes it with the data
         * read from the input stream inStream.
         */
        public X509Certificate ReadCertificate(Stream inStream)
        {
            if (inStream == null)
                throw new ArgumentNullException(nameof(inStream));
            if (!inStream.CanRead)
                throw new ArgumentException("Stream must be read-able", nameof(inStream));

            if (currentStream == null)
            {
                currentStream = inStream;
                sData = null;
                sDataObjectCount = 0;
            }
            else if (currentStream != inStream) // reset if input stream has changed
            {
                currentStream = inStream;
                sData = null;
                sDataObjectCount = 0;
            }

            try
            {
                if (sData != null)
                {
                    if (sDataObjectCount != sData.Count)
                        return GetCertificate();

                    sData = null;
                    sDataObjectCount = 0;
                    // TODO[api] Consider removing this and continuing directly
                    return null;
                }

                int tag = inStream.ReadByte();
                if (tag < 0)
                    return null;

                if (inStream.CanSeek)
                {
                    inStream.Seek(-1L, SeekOrigin.Current);
                }
                else
                {
                    PushbackStream pis = new PushbackStream(inStream);
                    pis.Unread(tag);
                    inStream = pis;
                }

                if (tag != 0x30)  // assume ascii PEM encoded.
                    return ReadPemCertificate(inStream);

                using (var asn1In = new Asn1InputStream(inStream, int.MaxValue, leaveOpen: true))
                {
                    return ReadDerCertificate(asn1In);
                }
            }
            catch (CertificateException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new CertificateException("Failed to read certificate", e);
            }
        }

        /**
         * Returns a (possibly empty) collection view of the certificates
         * read from the given input stream inStream.
         */
        public IList<X509Certificate> ReadCertificates(Stream inStream) =>
            new List<X509Certificate>(ParseCertificates(inStream));

        public IEnumerable<X509Certificate> ParseCertificates(Stream inStream)
        {
            X509Certificate cert;
            while ((cert = ReadCertificate(inStream)) != null)
            {
                yield return cert;
            }
        }
    }
}
