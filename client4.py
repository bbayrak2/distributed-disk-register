import socket
import time


class LeaderClient:
    def __init__(self, host="127.0.0.1", port=6666):
        self.host = host
        self.port = port
        self.used_ids = set()  # önceden kullandığımız id'leri kaydetmek için
    def send_command(self, command: str):
        command = command.strip()

        # SET ID çakışması olduğunda sunucuya göndermez
        if command.upper().startswith("SET "):
            try: 
                parts = command.split(" ", 2)
                message_id = int(parts[1])

                if message_id in self.used_ids:
                    print(f" HATA: {message_id} ID daha önce kullanıldı. Sunucuya gönderilmedi.")
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
        if cmd.lower() == "exit":
            break
        client.send_command(cmd)

# test için başlangıç ID'sini manuel gireceğiz
def load_test_set(client: LeaderClient):
    start_id = int(input("Başlangıç message_id girin (örn: 1): "))
    n = int(input("Kaç adet SET gönderilsin?: "))
    delay = 0.001

    current_id = start_id

    for _ in range(n):
        client.send_command(f"SET {current_id} mesaj_{current_id}")
        current_id += 1

    print(f"{n} adet SET gönderildi.")


if __name__ == "__main__":
    client = LeaderClient()

    print("1 - Manuel SET / GET")
    print("2 - Otomatik SET yük testi")

    choice = input("Seçim: ")

    if choice == "1":
        interactive_mode(client)
    elif choice == "2":
        load_test_set(client)
    else:
        print("Geçersiz seçim")
