using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Org.BouncyCastle.Asn1;
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
    /**
	 * The following extensions are listed in RFC 2459 as relevant to CRLs
	 *
	 * Authority Key Identifier
	 * Issuer Alternative Name
	 * CRL Number
	 * Delta CRL Indicator (critical)
	 * Issuing Distribution Point (critical)
	 */
    public class X509Crl
		: X509ExtensionBase
		// TODO Add interface Crl?
	{
        private class CachedEncoding
        {
            private readonly byte[] encoding;
            private readonly CrlException exception;

            internal CachedEncoding(byte[] encoding, CrlException exception)
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
                    throw new CrlException();

                return encoding;
            }
        }

        private readonly CertificateList c;
		private readonly byte[] sigAlgParams;
		private readonly bool isIndirect;

        private string m_sigAlgName = null;

        private CachedEncoding cachedEncoding;

        private volatile bool hashValueSet;
        private volatile int hashValue;

        public X509Crl(byte[] encoding)
            : this(CertificateList.GetInstance(encoding))
        {
        }

        public X509Crl(CertificateList c)
		{
			this.c = c;

			try
			{
                Asn1Encodable parameters = c.SignatureAlgorithm.Parameters;
                this.sigAlgParams = parameters?.GetEncoded(Asn1Encodable.Der);

                this.isIndirect = IsIndirectCrl;
			}
			catch (Exception e)
			{
				throw new CrlException("CRL contents invalid: " + e);
			}
		}

        public virtual CertificateList CertificateList
        {
            get { return c; }
        }

        protected override X509Extensions GetX509Extensions()
		{
			return c.Version >= 2
				?	c.TbsCertList.Extensions
				:	null;
		}

        // TODO[api] Rename 'key' to 'publicKey'
        public virtual bool IsSignatureValid(AsymmetricKeyParameter key)
        {
            return CheckSignatureValid(new Asn1VerifierFactory(c.SignatureAlgorithm, key));
        }

        public virtual bool IsSignatureValid(IVerifierFactoryProvider verifierProvider)
        {
            return CheckSignatureValid(verifierProvider.CreateVerifierFactory(c.SignatureAlgorithm));
        }

        public virtual bool IsAlternativeSignatureValid(IVerifierFactoryProvider verifierProvider)
        {
            var tbsCertList = c.TbsCertList;
            var extensions = tbsCertList.Extensions;

            AltSignatureAlgorithm altSigAlg = AltSignatureAlgorithm.FromExtensions(extensions);
            AltSignatureValue altSigValue = AltSignatureValue.FromExtensions(extensions);

            var verifier = verifierProvider.CreateVerifierFactory(altSigAlg.Algorithm);

            Asn1Sequence tbsSeq = Asn1Sequence.GetInstance(tbsCertList.ToAsn1Object());
            Asn1EncodableVector v = new Asn1EncodableVector();

            int start = 1;    //  want to skip signature field
            if (tbsSeq[0] is DerInteger version)
            {
                v.Add(version);
                start++;
            }

            for (int i = start; i < tbsSeq.Count - 1; i++)
            {
                v.Add(tbsSeq[i]);
            }

            v.Add(new DerTaggedObject(true, 0, extensions.ToAsn1ObjectTrimmed()));

			return X509Utilities.VerifySignature(verifier, new DerSequence(v), altSigValue.Signature);
        }

		public virtual void Verify(AsymmetricKeyParameter publicKey)
		{
			CheckSignature(new Asn1VerifierFactory(c.SignatureAlgorithm, publicKey));
		}

        /// <summary>
        /// Verify the CRL's signature using a verifier created using the passed in verifier provider.
        /// </summary>
        /// <param name="verifierProvider">An appropriate provider for verifying the CRL's signature.</param>
        /// <returns>True if the signature is valid.</returns>
        /// <exception cref="Exception">If verifier provider is not appropriate or the CRL algorithm is invalid.</exception>
        public virtual void Verify(IVerifierFactoryProvider verifierProvider)
        {
            CheckSignature(verifierProvider.CreateVerifierFactory(c.SignatureAlgorithm));
        }

        /// <summary>Verify the CRL's alternative signature using a verifier created using the passed in
        /// verifier provider.</summary>
        /// <param name="verifierProvider">An appropriate provider for verifying the CRL's alternative signature.
		/// </param>
        /// <exception cref="Exception">If verifier provider is not appropriate or the CRL alternative signature
        /// algorithm is invalid.</exception>
        public virtual void VerifyAltSignature(IVerifierFactoryProvider verifierProvider)
        {
            if (!IsAlternativeSignatureValid(verifierProvider))
                throw new InvalidKeyException("CRL alternative signature does not verify with supplied public key.");
        }

        protected virtual void CheckSignature(IVerifierFactory verifier)
        {
			if (!CheckSignatureValid(verifier))
                throw new InvalidKeyException("CRL does not verify with supplied public key.");
        }

        protected virtual bool CheckSignatureValid(IVerifierFactory verifier)
        {
            var tbsCertList = c.TbsCertList;

            if (!X509Utilities.AreEquivalentAlgorithms(c.SignatureAlgorithm, tbsCertList.Signature))
                throw new CrlException("Signature algorithm on CertificateList does not match TbsCertList.");

			return X509Utilities.VerifySignature(verifier, tbsCertList, c.Signature);
        }

        public virtual int Version
		{
			get { return c.Version; }
		}

		public virtual X509Name IssuerDN
		{
			get { return c.Issuer; }
		}

		public virtual DateTime ThisUpdate
		{
			get { return c.ThisUpdate.ToDateTime(); }
		}

		public virtual DateTime? NextUpdate => c.NextUpdate?.ToDateTime();

		private ISet<X509CrlEntry> LoadCrlEntries()
		{
			var entrySet = new HashSet<X509CrlEntry>();
			var revoked = c.GetRevokedCertificateEnumeration();

			X509Name previousCertificateIssuer = IssuerDN;
			foreach (CrlEntry entry in revoked)
			{
				X509CrlEntry crlEntry = new X509CrlEntry(entry, isIndirect, previousCertificateIssuer);
				entrySet.Add(crlEntry);
				previousCertificateIssuer = crlEntry.GetCertificateIssuer();
			}

			return entrySet;
		}

		public virtual X509CrlEntry GetRevokedCertificate(
			BigInteger serialNumber)
		{
			var certs = c.GetRevokedCertificateEnumeration();

			X509Name previousCertificateIssuer = IssuerDN;
			foreach (CrlEntry entry in certs)
			{
				X509CrlEntry crlEntry = new X509CrlEntry(entry, isIndirect, previousCertificateIssuer);

				if (serialNumber.Equals(entry.UserCertificate.Value))
				{
					return crlEntry;
				}

				previousCertificateIssuer = crlEntry.GetCertificateIssuer();
			}

			return null;
		}

		public virtual ISet<X509CrlEntry> GetRevokedCertificates()
		{
			var entrySet = LoadCrlEntries();

			if (entrySet.Count > 0)
				return entrySet;

			return null;
		}

		public virtual byte[] GetTbsCertList()
		{
			try
			{
				return c.TbsCertList.GetDerEncoded();
			}
			catch (Exception e)
			{
				throw new CrlException(e.ToString());
			}
		}

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

        public virtual string SigAlgOid
		{
            get { return c.SignatureAlgorithm.Algorithm.Id; }
		}

		public virtual byte[] GetSigAlgParams()
		{
			return Arrays.Clone(sigAlgParams);
		}

        public virtual AlgorithmIdentifier SignatureAlgorithm => c.SignatureAlgorithm;

        /// <summary>
        /// Return the DER encoding of this CRL.
        /// </summary>
        /// <returns>A byte array containing the DER encoding of this CRL.</returns>
        /// <exception cref="CrlException">If there is an error encoding the CRL.</exception>
        public virtual byte[] GetEncoded()
        {
            return Arrays.Clone(GetCachedEncoding().GetEncoded());
        }

        public override bool Equals(object other)
		{
            if (this == other)
                return true;

            X509Crl that = other as X509Crl;
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

		/**
		 * Returns a string representation of this CRL.
		 *
		 * @return a string representation of this CRL.
		 */
		public override string ToString()
		{
			StringBuilder buf = new StringBuilder();

			buf.Append("              Version: ").Append(this.Version).AppendLine();
			buf.Append("             IssuerDN: ").Append(this.IssuerDN).AppendLine();
			buf.Append("          This update: ").Append(this.ThisUpdate).AppendLine();
			buf.Append("          Next update: ").Append(this.NextUpdate).AppendLine();
			buf.Append("  Signature Algorithm: ").Append(this.SigAlgName).AppendLine();

			byte[] sig = this.GetSignature();

			buf.Append("            Signature: ");
			buf.AppendLine(Hex.ToHexString(sig, 0, 20));

			for (int i = 20; i < sig.Length; i += 20)
			{
				int count = System.Math.Min(20, sig.Length - i);
				buf.Append("                       ");
				buf.AppendLine(Hex.ToHexString(sig, i, count));
			}

			X509Extensions extensions = c.TbsCertList.Extensions;

			if (extensions != null)
			{
				var e = extensions.ExtensionOids.GetEnumerator();

				if (e.MoveNext())
				{
					buf.AppendLine("           Extensions:");
				}

				do
				{
					DerObjectIdentifier oid = e.Current;
					X509Extension ext = extensions.GetExtension(oid);

					if (ext.Value != null)
					{
						Asn1Object asn1Value = X509ExtensionUtilities.FromExtensionValue(ext.Value);

						buf.Append("                       critical(").Append(ext.IsCritical).Append(") ");
						try
						{
							if (oid.Equals(X509Extensions.CrlNumber))
							{
								buf.Append(new CrlNumber(DerInteger.GetInstance(asn1Value).PositiveValue)).AppendLine();
							}
							else if (oid.Equals(X509Extensions.DeltaCrlIndicator))
							{
								buf.Append(
									"Base CRL: "
									+ new CrlNumber(DerInteger.GetInstance(
									asn1Value).PositiveValue))
									.AppendLine();
							}
							else if (oid.Equals(X509Extensions.IssuingDistributionPoint))
							{
								buf.Append(IssuingDistributionPoint.GetInstance((Asn1Sequence) asn1Value)).AppendLine();
							}
							else if (oid.Equals(X509Extensions.CrlDistributionPoints))
							{
								buf.Append(CrlDistPoint.GetInstance((Asn1Sequence) asn1Value)).AppendLine();
							}
							else if (oid.Equals(X509Extensions.FreshestCrl))
							{
								buf.Append(CrlDistPoint.GetInstance((Asn1Sequence) asn1Value)).AppendLine();
							}
							else
							{
								buf.Append(oid.Id);
								buf.Append(" value = ").Append(
									Asn1Dump.DumpAsString(asn1Value))
									.AppendLine();
							}
						}
						catch (Exception)
						{
							buf.Append(oid.Id);
							buf.Append(" value = ").Append("*****").AppendLine();
						}
					}
					else
					{
						buf.AppendLine();
					}
				}
				while (e.MoveNext());
			}

			var certSet = GetRevokedCertificates();
			if (certSet != null)
			{
				foreach (X509CrlEntry entry in certSet)
				{
					buf.Append(entry);
					buf.AppendLine();
				}
			}

			return buf.ToString();
		}

		/**
		 * Checks whether the given certificate is on this CRL.
		 *
		 * @param cert the certificate to check for.
		 * @return true if the given certificate is on this CRL,
		 * false otherwise.
		 */
		public virtual bool IsRevoked(X509Certificate cert)
		{
			CrlEntry[] certs = c.GetRevokedCertificates();

			if (certs != null)
			{
				BigInteger serial = cert.SerialNumber;

				for (int i = 0; i < certs.Length; i++)
				{
					if (certs[i].UserCertificate.HasValue(serial))
						return true;
				}
			}

			return false;
		}

		protected virtual bool IsIndirectCrl
		{
			get
			{
				IssuingDistributionPoint idp;
				try
				{
					idp = this.GetExtension(X509Extensions.IssuingDistributionPoint,
						IssuingDistributionPoint.GetInstance);
				}
				catch (Exception e)
				{
					// TODO
//					throw new ExtCrlException("Exception reading IssuingDistributionPoint", e);
					throw new CrlException("Exception reading IssuingDistributionPoint" + e);
				}

				return idp != null && idp.IsIndirectCrl;
			}
		}

        internal byte[] GetEncodedInternal() => GetCachedEncoding().GetEncoded();

        private CachedEncoding GetCachedEncoding() =>
			Objects.EnsureSingletonInitialized(ref cachedEncoding, c, CreateCachedEncoding);

		private static CachedEncoding CreateCachedEncoding(CertificateList c)
		{
            byte[] encoding = null;
            CrlException exception = null;
            try
            {
                encoding = c.GetEncoded(Asn1Encodable.Der);
            }
            catch (IOException e)
            {
                exception = new CrlException("Failed to DER-encode CRL", e);
            }

            return new CachedEncoding(encoding, exception);
        }
    }
}
