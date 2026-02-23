namespace GrygTools.AssetManagement
{
	public interface IPoolableObject
	{
		void InitPoolable();
		void ReturnPoolable();
	}
}