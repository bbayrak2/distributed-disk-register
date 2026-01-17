import socket
import time
from pathlib import Path


ana_dizin = Path(__file__).resolve().parent
kayıt_yolu = ana_dizin /"GrpcService"/"Records"
sırala=[]

class LeaderClient:
    def __init__(self, host="127.0.0.1", port=6666):
        self.host = host
        self.port = port
        self.path1 = kayıt_yolu
        if self.path1:
            ıd_tespit(self.path1) 
        self.used_ids = set(sırala) 
        
    def send_command(self, command: str):
        command = command.strip()

        if command.upper().startswith("SET "):
            try:
                parts = command.split(" ", 2)
                message_id = int(parts[1])

                if message_id in self.used_ids:
                    print(f" HATA: {message_id} ID daha önce kullanıldı. Sunucuya gönderilmedi.")
                    print(f"{max_ıd(self.path1)}")
                    return

                self.used_ids.add(message_id)

            except (IndexError, ValueError):
                print(" Hatalı SET formatı. Doğru kullanım: SET <id> <mesaj>")
                return

        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.connect((self.host, self.port))

                message = command + "\n"
                s.sendall(message.encode("utf-8"))

                response = s.recv(1024).decode("utf-8").strip()
                print("Sunucudan gelen cevap:", response)
                return response

        except ConnectionRefusedError:
            print(" Bağlantı reddedildi: Lider sunucu çalışmıyor olabilir.")
        except Exception as e:
            print("  Hata oluştu:", e)


def interactive_mode(client: LeaderClient):
    print("\nManuel mod")
    print("SET <id> <mesaj>")
    print("GET <id>")
    print("Çıkmak için: exit")

    while True:
        cmd = input("> ").strip()
        parts = cmd.split()
        if len(parts) == 0:
            continue
        sorgu= int(parts[1])

        if parts[0]=="GET":
            if sorgu in client.used_ids:
                pass
            else:
                print("lütfen geçerli bir ıd giriniz")
                continue 

        if cmd.lower() == "exit":
            break
        client.send_command(cmd)


def load_test_set(client: LeaderClient):
    
    n = int(input("Kaç adet SET gönderilsin?: "))

    if client.used_ids:
        baslangic_id = max(client.used_ids) + 1
    else:
        baslangic_id = 1
    
    current_id = baslangic_id

    for i in range(1, n + 1):
        client.send_command(f"SET {current_id} mesaj_{current_id}")
        current_id+=1
    
    print(f"{n} adet SET gönderildi.")


def max_ıd(yol):
    ıd_tespit(yol)
    if sırala:
        print(f"mevcut max ıd {max(sırala)}  \ndaha buyuk bir id giriniz")
def ıd_tespit(yol):
 
    ana_yol = Path(yol)
    if not ana_yol.exists():
        print("Yol bulunamadı.")
        return
    for klasor in ana_yol.iterdir():
        if klasor.is_dir():
            ıd_tespit(klasor.resolve())
        else:
            if klasor.is_file():
                    sıra =int(klasor.stem)
                    sırala.append(sıra)
                    


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


if __name__ == "__main__":
    client = LeaderClient()

    print("1 - Manuel SET / GET")
    print("2 - Otomatik SET yük testi")
    print("3 - Mesaj Verisi ")

    choice = input("Seçim: ")

    if choice == "1":
        interactive_mode(client)
    elif choice == "2":
        load_test_set(client)
    elif choice == "3":
        mesaj_sayisi_pathlib(client.path1)
    else:
        print("Geçersiz seçim")
