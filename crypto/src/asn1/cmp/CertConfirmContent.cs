namespace Org.BouncyCastle.Asn1.Cmp
{
    public class CertConfirmContent
		: Asn1Encodable
	{
        public static CertConfirmContent GetInstance(object obj)
        {
            if (obj == null)
                return null;
            if (obj is CertConfirmContent certConfirmContent)
                return certConfirmContent;
            return new CertConfirmContent(Asn1Sequence.GetInstance(obj));
        }

        public static CertConfirmContent GetInstance(Asn1TaggedObject taggedObject, bool declaredExplicit) =>
            new CertConfirmContent(Asn1Sequence.GetInstance(taggedObject, declaredExplicit));

        public static CertConfirmContent GetTagged(Asn1TaggedObject taggedObject, bool declaredExplicit) =>
            new CertConfirmContent(Asn1Sequence.GetTagged(taggedObject, declaredExplicit));

        private readonly Asn1Sequence m_content;

        private CertConfirmContent(Asn1Sequence seq)
        {
            m_content = seq;
        }

        public virtual CertStatus[] ToCertStatusArray() => m_content.MapElements(CertStatus.GetInstance);

        /**
		 * <pre>
		 * CertConfirmContent ::= SEQUENCE OF CertStatus
		 * </pre>
		 * @return a basic ASN.1 object representation.
		 */
        public override Asn1Object ToAsn1Object() => m_content;
	}
}
