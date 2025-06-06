﻿using System;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Crmf;
using Org.BouncyCastle.Crypto;

namespace Org.BouncyCastle.Crmf
{
    public class CertificateRequestMessage
    {
        public static readonly int popRaVerified = Asn1.Crmf.ProofOfPossession.TYPE_RA_VERIFIED;
        public static readonly int popSigningKey = Asn1.Crmf.ProofOfPossession.TYPE_SIGNING_KEY;
        public static readonly int popKeyEncipherment = Asn1.Crmf.ProofOfPossession.TYPE_KEY_ENCIPHERMENT;
        public static readonly int popKeyAgreement = Asn1.Crmf.ProofOfPossession.TYPE_KEY_AGREEMENT;

        private readonly CertReqMsg m_certReqMsg;
        private readonly Controls m_controls;

        private static CertReqMsg ParseBytes(byte[] encoding) => CertReqMsg.GetInstance(encoding);

        /// <summary>
        /// Create a CertificateRequestMessage from the passed in bytes.
        /// </summary>
        /// <param name="encoded">BER/DER encoding of the CertReqMsg structure.</param>
        public CertificateRequestMessage(byte[] encoded)
            : this(ParseBytes(encoded))
        {
        }

        public CertificateRequestMessage(CertReqMsg certReqMsg)
        {
            m_certReqMsg = certReqMsg;
            m_controls = certReqMsg.CertReq.Controls;
        }

        /// <summary>
        /// Return the underlying ASN.1 object defining this CertificateRequestMessage object.
        /// </summary>
        /// <returns>A CertReqMsg</returns>
        public CertReqMsg ToAsn1Structure() => m_certReqMsg;

        /// <summary>
        /// Return the certificate request ID for this message.
        /// </summary>
        /// <returns>the certificate request ID.</returns>
        public DerInteger GetCertReqID() => m_certReqMsg.CertReq.CertReqID;

        /// <summary>
        /// Return the certificate template contained in this message.
        /// </summary>
        /// <returns>a CertTemplate structure.</returns>
        public CertTemplate GetCertTemplate() => m_certReqMsg.CertReq.CertTemplate;

        /// <summary>
        /// Return whether or not this request has control values associated with it.
        /// </summary>
        /// <returns>true if there are control values present, false otherwise.</returns>
        public bool HasControls => m_controls != null;

        /// <summary>
        /// Return whether or not this request has a specific type of control value.
        /// </summary>
        /// <param name="objectIdentifier">the type OID for the control value we are checking for.</param>
        /// <returns>true if a control value of type is present, false otherwise.</returns>
        public bool HasControl(DerObjectIdentifier objectIdentifier) => FindControl(objectIdentifier) != null;

        /// <summary>
        /// Return a control value of the specified type.
        /// </summary>
        /// <param name="type">the type OID for the control value we are checking for.</param>
        /// <returns>the control value if present, null otherwise.</returns>
        public IControl GetControl(DerObjectIdentifier type)
        {
            AttributeTypeAndValue found = FindControl(type);
            if (found != null)
            {
                var oid = found.Type;

                if (CrmfObjectIdentifiers.id_regCtrl_pkiArchiveOptions.Equals(oid))
                    return new PkiArchiveControl(PkiArchiveOptions.GetInstance(found.Value));

                if (CrmfObjectIdentifiers.id_regCtrl_regToken.Equals(oid))
                    return new RegTokenControl(DerUtf8String.GetInstance(found.Value));

                if (CrmfObjectIdentifiers.id_regCtrl_authenticator.Equals(oid))
                    return new AuthenticatorControl(DerUtf8String.GetInstance(found.Value));
            }
            return null;
        }

        public AttributeTypeAndValue FindControl(DerObjectIdentifier type)
        {
            if (m_controls == null)
                return null;

            AttributeTypeAndValue[] tAndV = m_controls.ToAttributeTypeAndValueArray();
            AttributeTypeAndValue found = null;

            for (int i = 0; i < tAndV.Length; i++)
            {
                if (tAndV[i].Type.Equals(type))
                {
                    found = tAndV[i];
                    break;
                }
            }

            return found;
        }

        /// <summary>
        /// Return whether or not this request message has a proof-of-possession field in it.
        /// </summary>
        /// <returns>true if proof-of-possession is present, false otherwise.</returns>
        public bool HasProofOfPossession => m_certReqMsg.Pop != null;

        /// <summary>
        /// Return the type of the proof-of-possession this request message provides.
        /// </summary>
        /// <returns>one of: popRaVerified, popSigningKey, popKeyEncipherment, popKeyAgreement</returns>
        public int ProofOfPossession => m_certReqMsg.Pop.Type;

        /// <summary>
        /// Return whether or not the proof-of-possession (POP) is of the type popSigningKey and
        /// it has a public key MAC associated with it.
        /// </summary>
        /// <returns>true if POP is popSigningKey and a PKMAC is present, false otherwise.</returns>
        public bool HasSigningKeyProofOfPossessionWithPkMac
        {
            get
            {
                ProofOfPossession pop = m_certReqMsg.Pop;

                if (pop.Type != popSigningKey)
                    return false;

                PopoSigningKey popoSign = PopoSigningKey.GetInstance(pop.Object);

                return popoSign.PoposkInput.PublicKeyMac != null;
            }
        }

        /// <summary>
        /// Return whether or not a signing key proof-of-possession (POP) is valid.
        /// </summary>
        /// <param name="verifierProvider">a provider that can produce content verifiers for the signature contained in this POP.</param>
        /// <returns>true if the POP is valid, false otherwise.</returns>
        /// <exception cref="InvalidOperationException">if there is a problem in verification or content verifier creation.</exception>
        /// <exception cref="InvalidOperationException">if POP not appropriate.</exception>
        public bool IsValidSigningKeyPop(IVerifierFactoryProvider verifierProvider)
        {
            ProofOfPossession pop = m_certReqMsg.Pop;
            if (pop.Type != popSigningKey)
                throw new InvalidOperationException("not Signing Key type of proof of possession");

            PopoSigningKey popoSign = PopoSigningKey.GetInstance(pop.Object);
            if (popoSign.PoposkInput != null && popoSign.PoposkInput.PublicKeyMac != null)
                throw new InvalidOperationException("verification requires password check");

            return VerifySignature(verifierProvider, popoSign);
        }

        /// <summary>
        /// Return whether or not a signing key proof-of-possession (POP), with an associated PKMAC, is valid.
        /// </summary>
        /// <param name="verifierProvider">a provider that can produce content verifiers for the signature contained in this POP.</param>
        /// <param name="macBuilder">a suitable PKMacBuilder to create the MAC verifier.</param>
        /// <param name="password">the password used to key the MAC calculation.</param>
        /// <returns>true if the POP is valid, false otherwise.</returns>
        /// <exception cref="InvalidOperationException">if there is a problem in verification or content verifier creation.</exception>
        /// <exception cref="InvalidOperationException">if POP not appropriate.</exception>
        public bool IsValidSigningKeyPop(IVerifierFactoryProvider verifierProvider, PKMacBuilder macBuilder,
            char[] password)
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            return IsValidSigningKeyPop(verifierProvider, macBuilder, password.AsSpan());
#else
            ProofOfPossession pop = m_certReqMsg.Pop;
            if (pop.Type != popSigningKey)
                throw new InvalidOperationException("not Signing Key type of proof of possession");

            PopoSigningKey popoSign = PopoSigningKey.GetInstance(pop.Object);
            if (popoSign.PoposkInput == null || popoSign.PoposkInput.Sender != null)
                throw new InvalidOperationException("no PKMAC present in proof of possession");

            PKMacValue mac = popoSign.PoposkInput.PublicKeyMac;
            PKMacValueVerifier macVerifier = new PKMacValueVerifier(macBuilder);

            return macVerifier.IsValid(mac, password, GetCertTemplate().PublicKey)
                && VerifySignature(verifierProvider, popoSign);
#endif
        }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        /// <summary>
        /// Return whether or not a signing key proof-of-possession (POP), with an associated PKMAC, is valid.
        /// </summary>
        /// <param name="verifierProvider">a provider that can produce content verifiers for the signature contained in this POP.</param>
        /// <param name="macBuilder">a suitable PKMacBuilder to create the MAC verifier.</param>
        /// <param name="password">the password used to key the MAC calculation.</param>
        /// <returns>true if the POP is valid, false otherwise.</returns>
        /// <exception cref="InvalidOperationException">if there is a problem in verification or content verifier creation.</exception>
        /// <exception cref="InvalidOperationException">if POP not appropriate.</exception>
        public bool IsValidSigningKeyPop(IVerifierFactoryProvider verifierProvider, PKMacBuilder macBuilder,
            ReadOnlySpan<char> password)
        {
            ProofOfPossession pop = m_certReqMsg.Pop;
            if (pop.Type != popSigningKey)
                throw new InvalidOperationException("not Signing Key type of proof of possession");

            PopoSigningKey popoSign = PopoSigningKey.GetInstance(pop.Object);
            if (popoSign.PoposkInput == null || popoSign.PoposkInput.Sender != null)
                throw new InvalidOperationException("no PKMAC present in proof of possession");

            PKMacValue mac = popoSign.PoposkInput.PublicKeyMac;
            PKMacValueVerifier macVerifier = new PKMacValueVerifier(macBuilder);

            return macVerifier.IsValid(mac, password, GetCertTemplate().PublicKey)
                && VerifySignature(verifierProvider, popoSign);
        }
#endif

        private bool VerifySignature(IVerifierFactoryProvider verifierFactoryProvider, PopoSigningKey signKey)
        {
            var verifierFactory = verifierFactoryProvider.CreateVerifierFactory(signKey.AlgorithmIdentifier);

            Asn1Encodable asn1Encodable = signKey.PoposkInput;
            if (asn1Encodable == null)
            {
                asn1Encodable = m_certReqMsg.CertReq;
            }

            return X509.X509Utilities.VerifySignature(verifierFactory, asn1Encodable, signKey.Signature);
        }

        /// <summary>
        /// Return the ASN.1 encoding of the certReqMsg we wrap.
        /// </summary>
        /// <returns>a byte array containing the binary encoding of the certReqMsg.</returns>
        public byte[] GetEncoded() => m_certReqMsg.GetEncoded();
    }
}
