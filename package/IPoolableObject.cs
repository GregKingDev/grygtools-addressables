namespace GrygTools.Pooling
{
	public interface IPoolableObject
	{
		void InitPoolable();
		void ReturnPoolable();
	}
}