using UnityEngine;

namespace QuickPocket
{
    // =========================================================================
    // FunnyMessages — mensagens engracacas exibidas no HUD quando o item
    // e rejeitado por TooLarge.
    //
    // Tres niveis de humor calibrados pelo ratio (maxDim / MaxItemBoundsSize):
    //   < 1.5  → frases leves  (item so um pouco acima do limite)
    //   1.5-3  → frases medias (claramente grande demais)
    //   > 3.0  → frases exageradas (absurdamente grande)
    // =========================================================================
    internal static class FunnyMessages
    {
        // ── Leve: item ligeiramente acima do limite ───────────────────────────
        private static readonly string[] _light =
        {
            "Esse ai ta um pouquinho grande pro bolso.",
            "Cabe, mas vai apertar.",
            "Nao e muito nao, mas ta grande.",
            "Meio grandinho esse ai.",
            "Ta passando do limite um pouquinho.",
            "Quase cabia.",
            "Ta na beira, campeao.",
            "Esse ai sobressai do bolso.",
            "Da pra tentar, mas nao da.",
            "So um cadinho maior que o bolso.",
            "Ligeiramente grande demais.",
            "Quase, quase...",
            "Nao chegou, mas chegou perto.",
            "Meio duvidoso esse tamanho.",
            "Isso aqui pede um bolso maior.",
            "Um milimetro a mais e nao entrava de jeito nenhum.",
            "Ta no limite do aceitavel.",
            "Isso ai vai rasgar o forro.",
            "Bolso respeita seus limites.",
            "Beira do limite, chefe.",
            "Isso ai e tamanho do bolso mais um.",
            "Tentou colocar, nao coube.",
            "Nao e o tamanho ideal.",
            "Na boa, ta grande.",
            "Nem forcando entra.",
            "O bolso disse 'nao, obrigado'.",
            "Mais um centimetro e dava.",
            "Reducao de 1% ja resolvia.",
            "Isso aqui bate na borda do bolso.",
            "Tao perto e tao longe ao mesmo tempo.",
        };

        // ── Medio: item claramente grande demais ─────────────────────────────
        private static readonly string[] _medium =
        {
            "Vai querer levar um bicho grande desse?",
            "Pesado demais, homi.",
            "E um bolso ou uma bolsa?",
            "Rapaz, isso ai vai rasgar o bolso.",
            "Quer guardar ou fazer mudanca?",
            "Esse ai precisa de um caminhao, nao de um bolso.",
            "Isso nao e objeto de bolso, e objeto de armazem.",
            "Relaxa, nao e porta-malas.",
            "Bolso nao e deposito, nao.",
            "Esse objeto tem complexo de gigante.",
            "Guarda num armario, nao no bolso.",
            "Esse negocio e enorme!",
            "Voce confundiu bolso com caminhao.",
            "Isso ai precisaria de um guindaste pra guardar.",
            "Vai precisar de uma mochila do tamanho de um onibus.",
            "Isso ai nao e de guardar, e de expor em museu.",
            "Bolso recusou, foi mocao de rejeicao unanime.",
            "A fisica nao permite isso.",
            "Chamou de bolso, mas e bau que voce quer.",
            "Isso daqui manda recado pro bolso: 'nem tente'.",
            "Eita, grande demais.",
            "Meu bolso nao e magico, nao.",
            "Eu guardaria, mas o bolso negou.",
            "Nao e pessoal, e o tamanho mesmo.",
            "Esse ai fica de fora.",
            "Isso nao e pra bolso, e pra hangar.",
            "Isso ai e quase do tamanho do jogador.",
            "Esse objeto nao sabe que e grande.",
            "Bolso mandou a conta separada. Nao cabe.",
            "Bolso disse nao. Sem direito a recurso.",
            "Isso aqui e pra deixar no chao mesmo.",
            "Esse item tem autoestima de gigante.",
            "Nao me pergunte como guardar isso.",
            "Isso cabe numa mansao. Nao no bolso.",
            "Esse item precisa de seu proprio ZIP code.",
        };

        // ── Pesado: item absurdamente grande ─────────────────────────────────
        private static readonly string[] _heavy =
        {
            "Nem jumento carrega isso tudo.",
            "Isso ai nao cabe nem no porta-malas.",
            "Esse bolso nao tem alvara pra carga pesada.",
            "Calma ai, campeao, isso e grande demais.",
            "E um bolso ou uma garagem?",
            "Isso aqui e do tamanho de um armario!",
            "Voce ta de brincadeira com esse tamanho?",
            "Precisaria de um pocket do Doraemon.",
            "Isso ia precisar de uma dimensao paralela pra guardar.",
            "Nem com magica esse negocio entra.",
            "Isso ai nao e pra guardar, e pra decorar o mapa.",
            "Voce queria guardar um predio no bolso?",
            "Isso ocupa mais espaco que o inventario inteiro.",
            "O objeto riu na sua cara.",
            "Esse negocio e uma construcao, nao um item.",
            "Chama um frete, porque bolso nao resolve.",
            "Isso aqui tem mais metros quadrados que um apartamento.",
            "Impossivel, astronomico, absurdo.",
            "Isso nem cabe num caminhao-bau.",
            "O bolso entrou em colapso existencial so de ver.",
            "Isso ai precisaria de uma orbita pra guardar.",
            "IMPOSSIVEL. Tamanho descomunal.",
            "Isso ai tem mais volume que o jogador inteiro.",
            "Voce queria engolir um elefante?",
            "Esse item e do tamanho de um bairro.",
            "Nao e bolso, nao e armario, nao e armazem. Que e isso?",
            "Esse objeto desafia a gravidade e o bom senso.",
            "Tem certeza que esse e um item e nao e um bioma?",
            "Isso ai nao cabe nem no jogo inteiro.",
            "Meu bolso mandou um SOS.",
            "Isso ai e GRANDE demais.",
            "Ta de sacanagem com esse tamanho.",
            "Nem tentei colocar, so de olhar deu pra ver.",
            "Amigo, isso aqui nao e engenharia civil.",
            "Bolso disse nao. Com maiuscula.",
            "Esse item tem ambicoes demais.",
            "Tamanho inesperado. Bolso traumatizado.",
            "Isso ai quebrou a regua de tamanho.",
            "Impossivel de guardar, possivel de admirar de longe.",
            "O bolso ta bem obrigado, sem esse fardo.",
        };

        // ── API publica ───────────────────────────────────────────────────────

        /// <summary>
        /// Retorna uma frase aleatoria para rejeicao por tamanho.
        /// ratio = maxDim / MaxItemBoundsSize (sempre >= 1.0 quando chamado).
        /// </summary>
        internal static string GetTooLargeMessage(float ratio)
        {
            string[] pool;
            if      (ratio < 1.5f) pool = _light;
            else if (ratio < 3.0f) pool = _medium;
            else                   pool = _heavy;

            return pool[Random.Range(0, pool.Length)];
        }
    }
}
