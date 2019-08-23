// 8 May 2019 
//
// Nani 2019
//
// Centralized static readonly XmlSerialisers for any given typs. This saves memory use.
// And speeds things up. XmlSerializers are thread safe and can be reused.
// XmlSerializers do not dispose well. Recreating new ones over and over will
// cause a memory leak when you make many. 

using System.Xml.Serialization;

namespace OpenSim.Framework
{
    public static class osXmlSerializer<T>
    {
        public static readonly XmlSerializer ForType = new XmlSerializer(typeof(T));
    }
}
