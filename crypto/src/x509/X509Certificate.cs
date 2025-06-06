using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Misc;
using Org.BouncyCastle.Asn1.Utilities;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Security.Certificates;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.X509.Extension;

namespace Org.BouncyCastle.X509
{
    /// <summary>
    /// An Object representing an X509 Certificate.
    /// Has static methods for loading Certificates encoded in many forms that return X509Certificate Objects.
    /// </summary>
    public class X509Certificate
        : X509ExtensionBase
    //		, PKCS12BagAttributeCarrier
    {
        private class CachedEncoding
        {
            private readonly byte[] encoding;
            private readonly CertificateEncodingException exception;

            internal CachedEncoding(byte[] encoding, CertificateEncodingException exception)
            {
                this.encoding = encoding;
                this.exception = exception;
            }

            internal byte[] Encoding
            {
                get { return encoding; }
            }

            internal byte[] GetEncoded()
            {
                if (null != exception)
                    throw exception;

                if (null == encoding)
                    throw new CertificateEncodingException();

                return encoding;
            }
        }

        private readonly X509CertificateStructure c;
        //private Dictionary<> pkcs12Attributes = new Dictionary<>();
        //private List<> pkcs12Ordering = new List<>();
        private readonly byte[] sigAlgParams;
        private readonly BasicConstraints basicConstraints;
        private readonly bool[] keyUsage;

        private string m_sigAlgName = null;

        private AsymmetricKeyParameter publicKeyValue;
        private CachedEncoding cachedEncoding;

        private volatile bool hashValueSet;
        private volatile int hashValue;

        protected X509Certificate()
        {
        }

        public X509Certificate(byte[] certData)
            : this(X509CertificateStructure.GetInstance(certData))
        {
        }

        // TODO[api] Change parameter name to 'certificate'
        public X509Certificate(X509CertificateStructure c)
        {
            this.c = c ?? throw new ArgumentNullException(nameof(c));

            try
            {
                Asn1Encodable parameters = c.SignatureAlgorithm.Parameters;
                this.sigAlgParams = parameters?.GetEncoded(Asn1Encodable.Der);
            }
            catch (Exception e)
            {
                throw new CertificateParsingException("Certificate contents invalid: " + e);
            }

            try
            {
                basicConstraints = this.GetExtension(X509Extensions.BasicConstraints, BasicConstraints.GetInstance);
            }
            catch (Exception e)
            {
                throw new CertificateParsingException("cannot construct BasicConstraints: " + e);
            }

            try
            {
                DerBitString bits = this.GetExtension(X509Extensions.KeyUsage, DerBitString.GetInstance);
                if (bits != null)
                {
                    byte[] bytes = bits.GetBytes();
                    int length = (bytes.Length * 8) - bits.PadBits;

                    keyUsage = new bool[(length < 9) ? 9 : length];

                    for (int i = 0; i != length; i++)
                    {
                        keyUsage[i] = (bytes[i / 8] & (0x80 >> (i % 8))) != 0;
                    }
                }
                else
                {
                    keyUsage = null;
                }
            }
            catch (Exception e)
            {
                throw new CertificateParsingException("cannot construct KeyUsage: " + e);
            }
        }

        //		internal X509Certificate(
        //			Asn1Sequence seq)
        //        {
        //            this.c = X509CertificateStructure.GetInstance(seq);
        //        }

        //		/// <summary>
        //        /// Load certificate from byte array.
        //        /// </summary>
        //        /// <param name="encoded">Byte array containing encoded X509Certificate.</param>
        //        public X509Certificate(
        //            byte[] encoded)
        //			: this((Asn1Sequence) new Asn1InputStream(encoded).ReadObject())
        //		{
        //        }
        //
        //        /// <summary>
        //        /// Load certificate from Stream.
        //        /// Must be positioned at start of certificate.
        //        /// </summary>
        //        /// <param name="input"></param>
        //        public X509Certificate(
        //            Stream input)
        //			: this((Asn1Sequence) new Asn1InputStream(input).ReadObject())
        //        {
        //        }

        public virtual X509CertificateStructure CertificateStructure
        {
            get { return c; }
        }

        /// <summary>
        /// Return true if the current time is within the start and end times nominated on the certificate.
        /// </summary>
        /// <returns>true id certificate is valid for the current time.</returns>
        public virtual bool IsValidNow
        {
            get { return IsValid(DateTime.UtcNow); }
        }

        /// <summary>
        /// Return true if the nominated time is within the start and end times nominated on the certificate.
        /// </summary>
        /// <param name="time">The time to test validity against.</param>
        /// <returns>True if certificate is valid for nominated time.</returns>
        public virtual bool IsValid(
            DateTime time)
        {
            return time.CompareTo(NotBefore) >= 0 && time.CompareTo(NotAfter) <= 0;
        }

        /// <summary>
        /// Checks if the current date is within certificate's validity period.
        /// </summary>
        public virtual void CheckValidity()
        {
            this.CheckValidity(DateTime.UtcNow);
        }

        /// <summary>
        /// Checks if the given date is within certificate's validity period.
        /// </summary>
        /// <exception cref="CertificateExpiredException">if the certificate is expired by given date</exception>
        /// <exception cref="CertificateNotYetValidException">if the certificate is not yet valid on given date</exception>
        public virtual void CheckValidity(
            DateTime time)
        {
            if (time.CompareTo(NotAfter) > 0)
                throw new CertificateExpiredException("certificate expired on " + c.EndDate);
            if (time.CompareTo(NotBefore) < 0)
                throw new CertificateNotYetValidException("certificate not valid until " + c.StartDate);
        }

        /// <summary>
        /// Return the certificate's version.
        /// </summary>
        /// <returns>An integer whose value Equals the version of the cerficate.</returns>
        public virtual int Version
        {
            get { return c.Version; }
        }

        /// <summary>
        /// Return a <see cref="Org.BouncyCastle.Math.BigInteger">BigInteger</see> containing the serial number.
        /// </summary>
        /// <returns>The Serial number.</returns>
        public virtual BigInteger SerialNumber
        {
            get { return c.SerialNumber.Value; }
        }

        /// <summary>
        /// Get the Issuer Distinguished Name. (Who signed the certificate.)
        /// </summary>
        /// <returns>And X509Object containing name and value pairs.</returns>
        //        public IPrincipal IssuerDN
        public virtual X509Name IssuerDN
        {
            get { return c.Issuer; }
        }

        /// <summary>
        /// Get the subject of this certificate.
        /// </summary>
        /// <returns>An X509Name object containing name and value pairs.</returns>
        //        public IPrincipal SubjectDN
        public virtual X509Name SubjectDN
        {
            get { return c.Subject; }
        }

        /// <summary>
        /// The time that this certificate is valid from.
        /// </summary>
        /// <returns>A DateTime object representing that time in the local time zone.</returns>
        public virtual DateTime NotBefore
        {
            get { return c.StartDate.ToDateTime(); }
        }

        /// <summary>
        /// The time that this certificate is valid up to.
        /// </summary>
        /// <returns>A DateTime object representing that time in the local time zone.</returns>
        public virtual DateTime NotAfter
        {
            get { return c.EndDate.ToDateTime(); }
        }

        public virtual TbsCertificateStructure TbsCertificate => c.TbsCertificate;

        /// <summary>
        /// Return the Der encoded TbsCertificate data.
        /// This is the certificate component less the signature.
        /// To Get the whole certificate call the GetEncoded() member.
        /// </summary>
        /// <returns>A byte array containing the Der encoded Certificate component.</returns>
        public virtual byte[] GetTbsCertificate()
        {
            return c.TbsCertificate.GetDerEncoded();
        }

        /// <summary>
        /// The signature.
        /// </summary>
        /// <returns>A byte array containg the signature of the certificate.</returns>
        public virtual byte[] GetSignature()
        {
            return c.GetSignatureOctets();
        }

        /// <summary>
		/// A meaningful version of the Signature Algorithm. (e.g. SHA1WITHRSA)
		/// </summary>
		/// <returns>A string representing the signature algorithm.</returns>
		public virtual string SigAlgName => Objects.EnsureSingletonInitialized(ref m_sigAlgName, SignatureAlgorithm,
            X509SignatureUtilities.GetSignatureName);

        /// <summary>
        /// Get the Signature Algorithms Object ID.
        /// </summary>
        /// <returns>A string containg a '.' separated object id.</returns>
        public virtual string SigAlgOid
        {
            get { return c.SignatureAlgorithm.Algorithm.Id; }
        }

        /// <summary>
        /// Get the signature algorithms parameters. (EG DSA Parameters)
        /// </summary>
        /// <returns>A byte array containing the Der encoded version of the parameters or null if there are none.</returns>
        public virtual byte[] GetSigAlgParams()
        {
            return Arrays.Clone(sigAlgParams);
        }

        /// <summary>The signature algorithm.</summary>
        public virtual AlgorithmIdentifier SignatureAlgorithm => c.SignatureAlgorithm;

        /// <summary>
        /// Get the issuers UID.
        /// </summary>
        /// <returns>A DerBitString.</returns>
        public virtual DerBitString IssuerUniqueID
        {
            get { return c.IssuerUniqueID; }
        }

        /// <summary>
        /// Get the subjects UID.
        /// </summary>
        /// <returns>A DerBitString.</returns>
        public virtual DerBitString SubjectUniqueID
        {
            get { return c.SubjectUniqueID; }
        }

        /// <summary>
        /// Get a key usage guidlines.
        /// </summary>
        public virtual bool[] GetKeyUsage()
        {
            return Arrays.Clone(keyUsage);
        }

        public virtual IList<DerObjectIdentifier> GetExtendedKeyUsage()
        {
            try
            {
                // TODO Use ExtendedKeyUsage type?
                Asn1Sequence seq = this.GetExtension(X509Extensions.ExtendedKeyUsage, Asn1Sequence.GetInstance);
                if (seq == null)
                    return null;

                var result = new List<DerObjectIdentifier>();
                foreach (var element in seq)
                {
                    result.Add(DerObjectIdentifier.GetInstance(element));
                }
                return result;
            }
            catch (Exception e)
            {
                throw new CertificateParsingException("error processing extended key usage extension", e);
            }
        }

        public virtual int GetBasicConstraints()
        {
            if (basicConstraints == null || !basicConstraints.IsCA())
                return -1;

            var pathLenConstraint = basicConstraints.PathLenConstraintInteger;
            if (pathLenConstraint == null)
                return int.MaxValue;

            return pathLenConstraint.IntPositiveValueExact;
        }

        public virtual GeneralNames GetIssuerAlternativeNameExtension() =>
            GetAlternativeNameExtension(X509Extensions.IssuerAlternativeName);

        public virtual GeneralNames GetSubjectAlternativeNameExtension() =>
            GetAlternativeNameExtension(X509Extensions.SubjectAlternativeName);

        public virtual IList<IList<object>> GetIssuerAlternativeNames() =>
            GetAlternativeNames(X509Extensions.IssuerAlternativeName);

        public virtual IList<IList<object>> GetSubjectAlternativeNames() =>
            GetAlternativeNames(X509Extensions.SubjectAlternativeName);

        // TODO[api] Remove protected access
        protected virtual GeneralNames GetAlternativeNameExtension(DerObjectIdentifier oid) =>
            this.GetExtension(oid, GeneralNames.GetInstance);

        // TODO[api] Remove protected access
        protected virtual IList<IList<object>> GetAlternativeNames(DerObjectIdentifier oid)
        {
            var generalNames = GetAlternativeNameExtension(oid);
            if (generalNames == null)
                return null;

            var gns = generalNames.GetNames();

            var result = new List<IList<object>>(gns.Length);
            foreach (GeneralName gn in gns)
            {
                var entry = new List<object>(2);
                entry.Add(gn.TagNo);

                switch (gn.TagNo)
                {
                case GeneralName.EdiPartyName:
                case GeneralName.X400Address:
                case GeneralName.OtherName:
                    entry.Add(gn.GetEncoded());
                    break;
                case GeneralName.DirectoryName:
                    // TODO Styles
                    //entry.Add(X509Name.GetInstance(Rfc4519Style.Instance, gn.Name).ToString());
                    entry.Add(X509Name.GetInstance(gn.Name).ToString());
                    break;
                case GeneralName.DnsName:
                case GeneralName.Rfc822Name:
                case GeneralName.UniformResourceIdentifier:
                    entry.Add(((IAsn1String)gn.Name).GetString());
                    break;
                case GeneralName.RegisteredID:
                    entry.Add(DerObjectIdentifier.GetInstance(gn.Name).Id);
                    break;
                case GeneralName.IPAddress:
                    byte[] addrBytes = Asn1OctetString.GetInstance(gn.Name).GetOctets();
                    IPAddress ipAddress = new IPAddress(addrBytes);
                    entry.Add(ipAddress.ToString());
                    break;
                default:
                    throw new IOException("Bad tag number: " + gn.TagNo);
                }

                result.Add(entry);
            }
            return result;
        }

        protected override X509Extensions GetX509Extensions() => c.Version >= 3 ? c.Extensions : null;

        /// <summary>
        /// Return the plain SubjectPublicKeyInfo that holds the encoded public key.
        /// </summary>
        public virtual SubjectPublicKeyInfo SubjectPublicKeyInfo => c.SubjectPublicKeyInfo;

        /// <summary>
        /// Get the public key of the subject of the certificate.
        /// </summary>
        /// <returns>The public key parameters.</returns>
        public virtual AsymmetricKeyParameter GetPublicKey()
        {
            // Cache the public key to support repeated-use optimizations
            return Objects.EnsureSingletonInitialized(ref publicKeyValue, c, CreatePublicKey);
        }

        /// <summary>
        /// Return the DER encoding of this certificate.
        /// </summary>
        /// <returns>A byte array containing the DER encoding of this certificate.</returns>
        /// <exception cref="CertificateEncodingException">If there is an error encoding the certificate.</exception>
        public virtual byte[] GetEncoded()
        {
            return Arrays.Clone(GetCachedEncoding().GetEncoded());
        }

        public override bool Equals(object other)
        {
            if (this == other)
                return true;

            X509Certificate that = other as X509Certificate;
            if (null == that)
                return false;

            if (this.hashValueSet && that.hashValueSet)
            {
                if (this.hashValue != that.hashValue)
                    return false;
            }
            else if (null == this.cachedEncoding || null == that.cachedEncoding)
            {
                DerBitString signature = c.Signature;
                if (null != signature && !signature.Equals(that.c.Signature))
                    return false;
            }

            byte[] thisEncoding = this.GetCachedEncoding().Encoding;
            byte[] thatEncoding = that.GetCachedEncoding().Encoding;

            return null != thisEncoding
                && null != thatEncoding
                && Arrays.AreEqual(thisEncoding, thatEncoding);
        }

        public override int GetHashCode()
        {
            if (!hashValueSet)
            {
                byte[] thisEncoding = this.GetCachedEncoding().Encoding;

                hashValue = Arrays.GetHashCode(thisEncoding);
                hashValueSet = true;
            }

            return hashValue;
        }

        //		public void setBagAttribute(
        //			DERObjectIdentifier oid,
        //			DEREncodable        attribute)
        //		{
        //			pkcs12Attributes.put(oid, attribute);
        //			pkcs12Ordering.addElement(oid);
        //		}
        //
        //		public DEREncodable getBagAttribute(
        //			DERObjectIdentifier oid)
        //		{
        //			return (DEREncodable)pkcs12Attributes.get(oid);
        //		}
        //
        //		public Enumeration getBagAttributeKeys()
        //		{
        //			return pkcs12Ordering.elements();
        //		}

        public override string ToString()
        {
            StringBuilder buf = new StringBuilder();

            buf.Append("  [0]         Version: ").Append(this.Version).AppendLine();
            buf.Append("         SerialNumber: ").Append(this.SerialNumber).AppendLine();
            buf.Append("             IssuerDN: ").Append(this.IssuerDN).AppendLine();
            buf.Append("           Start Date: ").Append(this.NotBefore).AppendLine();
            buf.Append("           Final Date: ").Append(this.NotAfter).AppendLine();
            buf.Append("            SubjectDN: ").Append(this.SubjectDN).AppendLine();
            buf.Append("           Public Key: ").Append(this.GetPublicKey()).AppendLine();
            buf.Append("  Signature Algorithm: ").Append(this.SigAlgName).AppendLine();

            byte[] sig = this.GetSignature();
            buf.Append("            Signature: ").AppendLine(Hex.ToHexString(sig, 0, 20));

            for (int i = 20; i < sig.Length; i += 20)
            {
                int len = System.Math.Min(20, sig.Length - i);
                buf.Append("                       ").AppendLine(Hex.ToHexString(sig, i, len));
            }

            X509Extensions extensions = c.Extensions;

            if (extensions != null)
            {
                var e = extensions.ExtensionOids.GetEnumerator();

                if (e.MoveNext())
                {
                    buf.AppendLine("       Extensions:");
                }

                do
                {
                    DerObjectIdentifier oid = e.Current;
                    X509Extension ext = extensions.GetExtension(oid);

                    if (ext.Value != null)
                    {
                        Asn1Object obj = X509ExtensionUtilities.FromExtensionValue(ext.Value);

                        buf.Append("                       critical(").Append(ext.IsCritical).Append(") ");
                        try
                        {
                            if (oid.Equals(X509Extensions.BasicConstraints))
                            {
                                buf.Append(BasicConstraints.GetInstance(obj));
                            }
                            else if (oid.Equals(X509Extensions.KeyUsage))
                            {
                                buf.Append(KeyUsage.GetInstance(obj));
                            }
                            else if (oid.Equals(MiscObjectIdentifiers.NetscapeCertType))
                            {
                                buf.Append(new NetscapeCertType((DerBitString)obj));
                            }
                            else if (oid.Equals(MiscObjectIdentifiers.NetscapeRevocationUrl))
                            {
                                buf.Append(new NetscapeRevocationUrl((DerIA5String)obj));
                            }
                            else if (oid.Equals(MiscObjectIdentifiers.VerisignCzagExtension))
                            {
                                buf.Append(new VerisignCzagExtension((DerIA5String)obj));
                            }
                            else
                            {
                                buf.Append(oid.Id);
                                buf.Append(" value = ").Append(Asn1Dump.DumpAsString(obj));
                                //buf.Append(" value = ").Append("*****").AppendLine();
                            }
                        }
                        catch (Exception)
                        {
                            buf.Append(oid.Id);
                            //buf.Append(" value = ").Append(new string(Hex.encode(ext.getValue().getOctets()))).AppendLine();
                            buf.Append(" value = ").Append("*****");
                        }
                    }

                    buf.AppendLine();
                }
                while (e.MoveNext());
            }

            return buf.ToString();
        }

        // TODO[api] Rename 'key' to 'publicKey'
        public virtual bool IsSignatureValid(AsymmetricKeyParameter key) =>
            CheckSignatureValid(new Asn1VerifierFactory(c.SignatureAlgorithm, key));

        public virtual bool IsSignatureValid(IVerifierFactoryProvider verifierProvider) =>
            CheckSignatureValid(verifierProvider.CreateVerifierFactory(c.SignatureAlgorithm));

        public virtual bool IsAlternativeSignatureValid(AsymmetricKeyParameter publicKey) =>
            IsAlternativeSignatureValid(new Asn1VerifierFactoryProvider(publicKey));

        public virtual bool IsAlternativeSignatureValid(IVerifierFactoryProvider verifierProvider)
        {
            var tbsCertificate = c.TbsCertificate;
            var extensions = tbsCertificate.Extensions;

            AltSignatureAlgorithm altSigAlg = AltSignatureAlgorithm.FromExtensions(extensions);
            AltSignatureValue altSigValue = AltSignatureValue.FromExtensions(extensions);

            var verifier = verifierProvider.CreateVerifierFactory(altSigAlg.Algorithm);

            Asn1Sequence tbsSeq = Asn1Sequence.GetInstance(tbsCertificate.ToAsn1Object());
            Asn1EncodableVector v = new Asn1EncodableVector();

            for (int i = 0; i < tbsSeq.Count - 1; i++)
            {
                if (i != 2) // signature field - must be ver 3 so version always present
                {
                    v.Add(tbsSeq[i]);
                }
            }

            v.Add(new DerTaggedObject(true, 3, extensions.ToAsn1ObjectTrimmed()));

            return X509Utilities.VerifySignature(verifier, new DerSequence(v), altSigValue.Signature);
        }

        /// <summary>
        /// Verify the certificate's signature using the nominated public key.
        /// </summary>
        /// <param name="key">An appropriate public key parameter object, RsaPublicKeyParameters, DsaPublicKeyParameters or ECDsaPublicKeyParameters</param>
        /// <returns>True if the signature is valid.</returns>
        /// <exception cref="Exception">If key submitted is not of the above nominated types.</exception>
        // TODO[api] Rename 'key' to 'publicKey'
        public virtual void Verify(AsymmetricKeyParameter key)
        {
            CheckSignature(new Asn1VerifierFactory(c.SignatureAlgorithm, key));
        }

        /// <summary>
        /// Verify the certificate's signature using a verifier created using the passed in verifier provider.
        /// </summary>
        /// <param name="verifierProvider">An appropriate provider for verifying the certificate's signature.</param>
        /// <exception cref="Exception">If verifier provider is not appropriate or the certificate signature algorithm
        /// is invalid.</exception>
        public virtual void Verify(IVerifierFactoryProvider verifierProvider)
        {
            CheckSignature(verifierProvider.CreateVerifierFactory(c.SignatureAlgorithm));
        }

        /// <summary>Verify the certificate's alternative signature using a verifier created using the passed in
        /// verifier provider.</summary>
        /// <param name="verifierProvider">An appropriate provider for verifying the certificate's alternative
        /// signature.</param>
        /// <exception cref="Exception">If verifier provider is not appropriate or the certificate alternative signature
        /// algorithm is invalid.</exception>
        public virtual void VerifyAltSignature(IVerifierFactoryProvider verifierProvider)
        {
            if (!IsAlternativeSignatureValid(verifierProvider))
                throw new InvalidKeyException("Public key presented not for certificate alternative signature");
        }

        protected virtual void CheckSignature(IVerifierFactory verifier)
        {
            if (!CheckSignatureValid(verifier))
                throw new InvalidKeyException("Public key presented not for certificate signature");
        }

        protected virtual bool CheckSignatureValid(IVerifierFactory verifier)
        {
            var tbsCertificate = c.TbsCertificate;

            if (!X509Utilities.AreEquivalentAlgorithms(c.SignatureAlgorithm, tbsCertificate.Signature))
                throw new CertificateException("signature algorithm in TBS cert not same as outer cert");

            return X509Utilities.VerifySignature(verifier, tbsCertificate, c.Signature);
        }

        internal byte[] GetEncodedInternal() => GetCachedEncoding().GetEncoded();

        private CachedEncoding GetCachedEncoding() =>
            Objects.EnsureSingletonInitialized(ref cachedEncoding, c, CreateCachedEncoding);

        private static CachedEncoding CreateCachedEncoding(X509CertificateStructure c)
        {
            byte[] encoding = null;
            CertificateEncodingException exception = null;
            try
            {
                encoding = c.GetEncoded(Asn1Encodable.Der);
            }
            catch (IOException e)
            {
                exception = new CertificateEncodingException("Failed to DER-encode certificate", e);
            }

            return new CachedEncoding(encoding, exception);
        }

        private static AsymmetricKeyParameter CreatePublicKey(X509CertificateStructure c) =>
            PublicKeyFactory.CreateKey(c.SubjectPublicKeyInfo);
    }
}
