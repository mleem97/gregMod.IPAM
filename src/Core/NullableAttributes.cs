namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    internal sealed class NullableContextAttribute : global::System.Attribute
    {
        public NullableContextAttribute(byte flag)
        {
        }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    internal sealed class NullableAttribute : global::System.Attribute
    {
        public NullableAttribute(byte flag)
        {
        }

        public NullableAttribute(byte[] flags)
        {
        }
    }
}
