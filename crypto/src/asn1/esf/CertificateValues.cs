using System;
using System.Collections.Generic;

using Org.BouncyCastle.Asn1.X509;

namespace Org.BouncyCastle.Asn1.Esf
{
    /// <remarks>
    /// RFC 3126: 4.3.1 Certificate Values Attribute Definition
    /// <code>
    /// CertificateValues ::= SEQUENCE OF Certificate
    /// </code>
    /// </remarks>
    public class CertificateValues
        : Asn1Encodable
    {
        public static CertificateValues GetInstance(object obj)
        {
            if (obj == null)
                return null;
            if (obj is CertificateValues certificateValues)
                return certificateValues;
            return new CertificateValues(Asn1Sequence.GetInstance(obj));
        }

        public static CertificateValues GetInstance(Asn1TaggedObject taggedObject, bool declaredExplicit) =>
            new CertificateValues(Asn1Sequence.GetInstance(taggedObject, declaredExplicit));

        public static CertificateValues GetTagged(Asn1TaggedObject taggedObject, bool declaredExplicit) =>
            new CertificateValues(Asn1Sequence.GetTagged(taggedObject, declaredExplicit));

        private readonly Asn1Sequence m_certificates;

        private CertificateValues(Asn1Sequence seq)
        {
            m_certificates = seq;
            m_certificates.MapElements(X509CertificateStructure.GetInstance); // Validate
        }

        public CertificateValues(params X509CertificateStructure[] certificates)
        {
            m_certificates = DerSequence.FromElements(certificates);
        }

        public CertificateValues(IEnumerable<X509CertificateStructure> certificates)
        {
            if (certificates == null)
                throw new ArgumentNullException(nameof(certificates));

            m_certificates = DerSequence.FromVector(Asn1EncodableVector.FromEnumerable(certificates));
        }

        public CertificateValues(IReadOnlyCollection<X509CertificateStructure> certificates)
        {
            if (certificates == null)
                throw new ArgumentNullException(nameof(certificates));

            m_certificates = DerSequence.FromCollection(certificates);
        }

        public X509CertificateStructure[] GetCertificates() =>
            m_certificates.MapElements(X509CertificateStructure.GetInstance);

        public override Asn1Object ToAsn1Object() => m_certificates;
    }
}
