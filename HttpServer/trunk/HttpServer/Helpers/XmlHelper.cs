// 13 May 2019
// - Nani changed some things.

using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace HttpServer.Helpers
{
    public static class TXmlSerializer<T>
    {
        public static readonly XmlSerializer ForType = new XmlSerializer(typeof(T));
    }

    public static class TXmlSerializer
    {
        private static readonly Dictionary<System.Type, XmlSerializer> Serializers = 
                            new Dictionary<System.Type, XmlSerializer>();

        public static XmlSerializer ForType(System.Type type)
        {
            try
            {
                if (Serializers.ContainsKey(type))
                    return Serializers[type];
            }
            catch { }

            lock (Serializers)
            {
                Serializers[type] = new XmlSerializer(type);
                return Serializers[type];
            }
        }
    }

    /// <summary>
    /// Helpers to make XML handling easier
    /// </summary>
    public static class XmlHelper
    {

        /// <summary>
        /// Serializes object to XML.
        /// </summary>
        /// <param name="value">object to serialize.</param>
        /// <returns>XML</returns>
        /// <remarks>
        /// Removes name spaces and adds indentation
        /// </remarks>
        public static string Serialize(object value)
        {
            Check.Require(value, "value");

            //These to lines are nessacary to get rid of the default namespaces.
            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add(string.Empty, string.Empty);

            // removing XML declaration, the default is false
            XmlWriterSettings xmlSettings = new XmlWriterSettings();
            xmlSettings.Indent = true;
            xmlSettings.IndentChars = "\t";
            xmlSettings.OmitXmlDeclaration = true;
           
            StringBuilder sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, xmlSettings))
            {
                TXmlSerializer.ForType(value.GetType()).Serialize(writer, value, ns);

                return sb.ToString();
            }
        }

        /// <summary>
        /// Create an object from a XML string
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="xml">XML string</param>
        /// <returns>object</returns>
        public static T Deserialize<T>(string xml)
        {
            Check.NotEmpty(xml, "xml");

            using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                return (T)TXmlSerializer<T>.ForType.Deserialize(stream);
            }
        }
    }
}
