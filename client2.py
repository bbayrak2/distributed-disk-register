import socket
import time


class LeaderClient:
    def __init__(self, host="127.0.0.1", port=6666):
        self.host = host
        self.port = port

    def send_command(self, command: str):
        
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.connect((self.host, self.port))

                message = command.strip() + "\n"
                s.sendall(message.encode("utf-8"))

                response = s.recv(4096).decode("utf-8").strip()
                print("Sunucudan gelen cevap:", response)
                return response

        except ConnectionRefusedError:
            print("Bağlantı reddedildi: Lider sunucu çalışmıyor olabilir.")
        except Exception as e:
            print("Hata oluştu:", e)


def interactive_mode(client: LeaderClient):
    
    print("Manuel mod (SET / GET)")
    print("Çıkmak için: exit")

    while True:
        cmd = input("> ")
        if cmd.lower() == "exit":
            break
        client.send_command(cmd)


def load_test_set(client: LeaderClient):
    
    n = int(input("Kaç adet SET gönderilsin?: "))
    delay = 0.001  

    for i in range(1, n + 1):
        client.send_command(f"SET {i} mesaj_{i}")
        time.sleep(delay)

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
