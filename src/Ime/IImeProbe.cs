namespace DaikonLine
{
    /// <summary>IME 状態の判定方式。方式を差し替え・追加できるようにインターフェースにしている。</summary>
    internal interface IImeProbe
    {
        /// <summary>
        /// 現在の入力先の IME 状態を判定する。監視スレッドから呼ばれる。
        /// </summary>
        /// <returns>判定結果。判定しない場合（自分自身が前面など）は null。</returns>
        ImeSnapshot Probe();
    }
}
