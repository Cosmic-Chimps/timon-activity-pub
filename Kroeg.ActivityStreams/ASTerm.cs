using System;
using Newtonsoft.Json.Linq;

namespace Kroeg.ActivityStreams
{
    public class ASTerm
    {
        public ASTerm() { }

        [Obsolete("Use ASTerm.MakePrimitive() instead")]
        public ASTerm(string value) { Primitive = value; }
        [Obsolete("Use ASTerm.MakePrimitive() instead")]
        public ASTerm(int value) { Primitive = value; }
        [Obsolete("Use ASTerm.MakePrimitive() instead")]
        public ASTerm(double value) { Primitive = value; }
        [Obsolete("Use ASTerm.MakePrimitive() instead")]
        public ASTerm(bool value) { Primitive = value; }
        
        [Obsolete("Use ASTerm.MakeSubObject() instead")]
        public ASTerm(ASObject value) { SubObject = value; }

        public const string NON_NEGATIVE_INTEGER = "http://www.w3.org/2001/XMLSchema#nonNegativeInteger";


        public static ASTerm MakePrimitive(object obj, string type = null)
        {
            return new ASTerm { Primitive = obj, Type = type };
        }

        public static ASTerm MakeId(string id)
        {
            return new ASTerm { Id = id };
        }

        public static ASTerm MakeSubObject(ASObject val)
        {
            return new ASTerm { SubObject = val };
        }

        public object Primitive { get; set; }
        public ASObject SubObject { get; set; }
        public string Language { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }

        /*
            has @type and no @value: is subobject
            has @id: reference to other object
            has @type and @value: primitive, but with special type
            has @value: primitive
         */

        public ASTerm Clone()
        {
            if (SubObject == null)
                return new ASTerm { Primitive = Primitive, Language = Language };
            else
                return new ASTerm { SubObject = SubObject.Clone(), Language = Language };
        }

        public static ASTerm Parse(JObject obj)
        {
            if (obj["@type"] != null && obj["@value"] == null)
                return new ASTerm { SubObject = ASObject.Parse(obj) };
            else if (obj["@id"] != null)
                return new ASTerm { Id = obj["@id"].ToObject<string>() };
            else if (obj["@type"] != null && obj["@value"] != null)
                return new ASTerm { Primitive = obj["@value"].ToObject<object>(), Type = obj["@type"].ToObject<string>() };
            else if (obj["@value"] != null)
                return new ASTerm { Primitive = obj["@value"].ToObject<object>() };
            throw new NotImplementedException();
        }

        public JObject Serialize(bool compact)
        {
            if (SubObject != null)
                return SubObject.Serialize(false, compact);
            else if (Id != null)
                return new JObject { ["@id"] = Id };
            else if (Primitive != null && Type != null)
                return new JObject { ["@value"] = JToken.FromObject(Primitive), ["@type"] = Type };
            else 
                return new JObject { ["@value"] = JToken.FromObject(Primitive) };
        }
    }
}
