def mesaj_sayisi_pathlib(yol):
    from pathlib import Path
    ana_yol = Path(yol)
    
    if not ana_yol.exists():
        print("Yol bulunamadı.")
        return

    print(f"--- Tarama Sonuçları ---\n")


    for klasor in ana_yol.iterdir():
        if klasor.is_dir():
            
            
            txt_sayisi = sum(1 for dosya in klasor.iterdir() 
                    if dosya.is_file() and dosya.suffix.lower() == '.txt')
            print(f"{klasor.name:<10} : {txt_sayisi} mesaj ")

