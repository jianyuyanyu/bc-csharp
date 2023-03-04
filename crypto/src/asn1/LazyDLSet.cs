using System;
using System.Collections.Generic;
using System.IO;

namespace Org.BouncyCastle.Asn1
{
    internal class LazyDLSet
        : DLSet
    {
        private byte[] m_encoded;

        internal LazyDLSet(byte[] encoded)
            : base()
        {
            if (null == encoded)
                throw new ArgumentNullException(nameof(encoded));

            m_encoded = encoded;
        }

        public override Asn1Encodable this[int index]
        {
            get
            {
                Force();

                return base[index];
            }
        }

        public override IEnumerator<Asn1Encodable> GetEnumerator()
        {
            byte[] encoded = GetContents();
            if (null != encoded)
                return new LazyDLEnumerator(encoded);

            return base.GetEnumerator();
        }

        public override int Count
        {
            get
            {
                Force();

                return base.Count;
            }
        }

        public override Asn1Encodable[] ToArray()
        {
            Force();

            return base.ToArray();
        }

        public override string ToString()
        {
            Force();

            return base.ToString();
        }

        internal override IAsn1Encoding GetEncoding(int encoding)
        {
            if (Asn1OutputStream.EncodingBer == encoding)
            {
                byte[] encoded = GetContents();
                if (null != encoded)
                    return new ConstructedLazyDLEncoding(Asn1Tags.Universal, Asn1Tags.Set, encoded);
            }
            else
            {
                Force();
            }

            return base.GetEncoding(encoding);
        }

        internal override IAsn1Encoding GetEncodingImplicit(int encoding, int tagClass, int tagNo)
        {
            if (Asn1OutputStream.EncodingBer == encoding)
            {
                byte[] encoded = GetContents();
                if (null != encoded)
                    return new ConstructedLazyDLEncoding(tagClass, tagNo, encoded);
            }
            else
            {
                Force();
            }

            return base.GetEncodingImplicit(encoding, tagClass, tagNo);
        }

        private void Force()
        {
            lock (this)
            {
                if (null != m_encoded)
                {
                    Asn1InputStream input = new LazyAsn1InputStream(m_encoded);
                    try
                    {
                        Asn1EncodableVector v = input.ReadVector();

                        m_elements = v.TakeElements();
                        m_sortedElements = m_elements.Length <= 1 ? m_elements : null;
                        m_encoded = null;
                    }
                    catch (IOException e)
                    {
                        throw new Asn1ParsingException("malformed ASN.1: " + e.Message, e);
                    }
                }
            }
        }

        private byte[] GetContents()
        {
            lock (this) return m_encoded;
        }
    }
}
